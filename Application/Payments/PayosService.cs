using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Payments
{
    public class PayosService
    {
        private readonly HttpClient _http;
        private readonly PayosOptions _options;
        private readonly ILogger<PayosService> _logger;

        public PayosService(HttpClient http, IOptions<PayosOptions> options, ILogger<PayosService> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public int GenerateOrderCode()
        {
            // PayOS requires an integer orderCode (Int32). Keep it within range while minimizing
            // collisions and avoiding any dependence on DB identity values (important when DB is recreated).
            //
            // Approach: millisecond-based modulo + 0-999 random jitter.
            // - baseCode changes every millisecond
            // - jitter reduces collision risk when multiple requests land in the same ms
            var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Keep comfortably under int.MaxValue.
            var baseCode = (int)(ms % 2_000_000_000L); // [0..1,999,999,999]
            if (baseCode <= 0) baseCode = 1;

            var jitter = RandomNumberGenerator.GetInt32(0, 1000); // 0-999
            return (baseCode / 1000) * 1000 + jitter;
        }

        public async Task<PayosPaymentLinkData> CreatePaymentLinkAsync(
            int amount,
            int orderCode,
            string description,
            string? returnUrlOverride = null,
            string? cancelUrlOverride = null,
            CancellationToken ct = default)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (orderCode <= 0) throw new ArgumentOutOfRangeException(nameof(orderCode));
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required", nameof(description));

            var returnUrl = string.IsNullOrWhiteSpace(returnUrlOverride) ? _options.ReturnUrl : returnUrlOverride;
            var cancelUrl = string.IsNullOrWhiteSpace(cancelUrlOverride) ? _options.CancelUrl : cancelUrlOverride;

            var request = new PayosCreatePaymentRequest
            {
                Amount = amount,
                OrderCode = orderCode,
                Description = description,
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl,
            };

            request.Signature = CreateSignatureForCreatePayment(request.Amount, request.CancelUrl, request.Description, request.OrderCode, request.ReturnUrl);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl.TrimEnd('/')), "/v2/payment-requests"))
            {
                Content = JsonContent.Create(request)
            };

            httpRequest.Headers.TryAddWithoutValidation("x-client-id", _options.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);

            using var response = await _http.SendAsync(httpRequest, ct);
            var payload = await response.Content.ReadFromJsonAsync<PayosCreatePaymentResponse>(cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayOS create payment failed: HTTP {Status} {BodyCode} {Desc}", (int)response.StatusCode, payload?.Code, payload?.Desc);
                throw new InvalidOperationException($"PayOS create payment failed: HTTP {(int)response.StatusCode}");
            }

            if (payload?.Data == null)
                throw new InvalidOperationException("PayOS create payment returned no data");

            return payload.Data;
        }

        public bool VerifyWebhookSignature(Dictionary<string, object?> data, string signature)
        {
            if (string.IsNullOrWhiteSpace(signature)) return false;

            // PayOS docs: signature = HMAC_SHA256( dataStringSortedByKeyAlphabet, checksumKey )
            // dataString format: key1=value1&key2=value2...
            var computed = CreateSignatureForWebhookData(data);
            return string.Equals(computed, signature, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<PayosPaymentLinkInfoData?> GetPaymentLinkInfoAsync(
            string id,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required", nameof(id));

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_options.BaseUrl.TrimEnd('/')), $"/v2/payment-requests/{Uri.EscapeDataString(id)}"));

            httpRequest.Headers.TryAddWithoutValidation("x-client-id", _options.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);

            using var response = await _http.SendAsync(httpRequest, ct);
            var payload = await response.Content.ReadFromJsonAsync<PayosGetPaymentLinkInfoResponse>(cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS get payment link info failed: HTTP {Status} {BodyCode} {Desc}",
                    (int)response.StatusCode,
                    payload?.Code,
                    payload?.Desc);
                return null;
            }

            if (payload?.Data == null)
            {
                _logger.LogWarning("PayOS get payment link info returned no data: {BodyCode} {Desc}", payload?.Code, payload?.Desc);
                return null;
            }

            return payload.Data;
        }

        public Task<PayosPaymentLinkInfoData?> GetPaymentLinkInfoAsync(
            int orderCode,
            CancellationToken ct = default)
        {
            if (orderCode <= 0) throw new ArgumentOutOfRangeException(nameof(orderCode));
            return GetPaymentLinkInfoAsync(orderCode.ToString(CultureInfo.InvariantCulture), ct);
        }

        private string CreateSignatureForCreatePayment(int amount, string cancelUrl, string description, int orderCode, string returnUrl)
        {
            // Docs example: amount=$amount&cancelUrl=$cancelUrl&description=$description&orderCode=$orderCode&returnUrl=$returnUrl
            var data = string.Join("&", new[]
            {
                $"amount={amount}",
                $"cancelUrl={cancelUrl}",
                $"description={description}",
                $"orderCode={orderCode}",
                $"returnUrl={returnUrl}",
            });

            return HmacSha256Hex(data, _options.ChecksumKey);
        }

        private string CreateSignatureForWebhookData(Dictionary<string, object?> data)
        {
            var sorted = data.OrderBy(kv => kv.Key, StringComparer.Ordinal);

            var parts = new List<string>();
            foreach (var (key, rawValue) in sorted)
            {
                var value = NormalizeWebhookValue(rawValue);
                parts.Add($"{key}={value}");
            }

            var dataString = string.Join("&", parts);
            return HmacSha256Hex(dataString, _options.ChecksumKey);
        }

        private static string NormalizeWebhookValue(object? value)
        {
            if (value == null) return "";

            // System.Text.Json deserializes unknown JSON values into JsonElement
            if (value is JsonElement el)
            {
                return el.ValueKind switch
                {
                    JsonValueKind.Null => "",
                    JsonValueKind.Undefined => "",
                    JsonValueKind.String => el.GetString() ?? "",
                    JsonValueKind.Number => el.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => el.GetRawText()
                };
            }

            if (value is string s)
            {
                if (string.Equals(s, "null", StringComparison.OrdinalIgnoreCase)) return "";
                if (string.Equals(s, "undefined", StringComparison.OrdinalIgnoreCase)) return "";
                return s;
            }

            if (value is bool b) return b ? "true" : "false";

            if (value is IFormattable f)
                return f.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString() ?? "";
        }

        private static string HmacSha256Hex(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
