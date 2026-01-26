using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Application.Payments
{
    public sealed class PayosCreatePaymentRequest
    {
        [JsonPropertyName("orderCode")]
        public int OrderCode { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;

        [JsonPropertyName("returnUrl")]
        public string ReturnUrl { get; set; } = null!;

        [JsonPropertyName("cancelUrl")]
        public string CancelUrl { get; set; } = null!;

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = null!;
    }

    public sealed class PayosCreatePaymentResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = null!;

        [JsonPropertyName("desc")]
        public string Desc { get; set; } = null!;

        [JsonPropertyName("data")]
        public PayosPaymentLinkData? Data { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }
    }

    public sealed class PayosPaymentLinkData
    {
        [JsonPropertyName("checkoutUrl")]
        public string CheckoutUrl { get; set; } = null!;

        [JsonPropertyName("qrCode")]
        public string? QrCode { get; set; }

        [JsonPropertyName("paymentLinkId")]
        public string? PaymentLinkId { get; set; }

        [JsonPropertyName("orderCode")]
        public int OrderCode { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public sealed class PayosGetPaymentLinkInfoResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = null!;

        [JsonPropertyName("desc")]
        public string Desc { get; set; } = null!;

        [JsonPropertyName("data")]
        public PayosPaymentLinkInfoData? Data { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }
    }

    public sealed class PayosPaymentLinkInfoData
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("orderCode")]
        public int OrderCode { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("amountPaid")]
        public int AmountPaid { get; set; }

        [JsonPropertyName("amountRemaining")]
        public int AmountRemaining { get; set; }

        // PayOS: PENDING | PAID | CANCELLED | EXPIRED
        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    public sealed class PayosWebhookPayload
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = null!;

        [JsonPropertyName("desc")]
        public string Desc { get; set; } = null!;

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public Dictionary<string, object?> Data { get; set; } = new();

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = null!;
    }
}
