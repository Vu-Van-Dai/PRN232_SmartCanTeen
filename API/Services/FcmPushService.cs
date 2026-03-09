using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace API.Services
{
    public class FcmPushService : IFcmPushService
    {
        private readonly ILogger<FcmPushService> _logger;
        private readonly FcmOptions _options;
        private readonly IHostEnvironment _env;
        private FirebaseApp? _app;
        private static bool _initialized;

        public FcmPushService(
            ILogger<FcmPushService> logger,
            IOptions<FcmOptions> options,
            IHostEnvironment env)
        {
            _logger = logger;
            _options = options.Value;
            _env = env;
        }

        private static string? TryDecodeBase64(string? base64)
        {
            var s = (base64 ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return null;

            try
            {
                var bytes = Convert.FromBase64String(s);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        private string? ResolveServiceAccountPath(string? path)
        {
            var p = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(p)) return null;

            if (Path.IsPathRooted(p)) return p;
            return Path.Combine(_env.ContentRootPath, p);
        }

        private bool EnsureInitialized()
        {
            if (_initialized) return true;

            if (_app != null)
            {
                _initialized = true;
                return true;
            }

            // If another component already initialized FirebaseApp, reuse it.
            try
            {
                var existing = FirebaseAdmin.FirebaseApp.DefaultInstance;
                if (existing != null)
                {
                    _app = existing;
                    _initialized = true;
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            var json = (_options.ServiceAccountJson ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                json = TryDecodeBase64(_options.ServiceAccountJsonBase64) ?? string.Empty;
            }

            var path = ResolveServiceAccountPath(_options.ServiceAccountJsonPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    _logger.LogInformation(
                        "FCM disabled: configure Firebase:ServiceAccountJson (recommended) or Firebase:ServiceAccountJsonBase64, or Firebase:ServiceAccountJsonPath.");
                    return false;
                }

                if (!File.Exists(path))
                {
                    _logger.LogWarning("FCM disabled: service account file not found at {Path}", path);
                    return false;
                }
            }

            lock (FirebaseAdminInitLock.Lock)
            {
                if (_initialized) return true;

                if (_app != null)
                {
                    _initialized = true;
                    return true;
                }

                try
                {
                    var existing = FirebaseAdmin.FirebaseApp.DefaultInstance;
                    if (existing != null)
                    {
                        _app = existing;
                        _initialized = true;
                        return true;
                    }
                }
                catch
                {
                    // ignore
                }

                try
                {
                    // Create default app once.
                    GoogleCredential credential;
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        credential = GoogleCredential.FromJson(json);
                    }
                    else
                    {
                        credential = GoogleCredential.FromFile(path!);
                    }

                    // Create default app once.
                    _app = FirebaseApp.Create(new AppOptions { Credential = credential });

                    _initialized = true;
                    _logger.LogInformation("FCM initialized successfully.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize FirebaseApp for FCM.");
                    return false;
                }
            }
        }

        public async Task SendOrderReadyAsync(
            IEnumerable<string> registrationTokens,
            Guid orderId,
            DateTime? pickupTime,
            CancellationToken cancellationToken = default)
        {
            if (!EnsureInitialized()) return;

            if (_app == null)
            {
                _logger.LogWarning("FCM send skipped: FirebaseApp not initialized.");
                return;
            }

            var tokens = registrationTokens
                .Select(t => (t ?? string.Empty).Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (tokens.Count == 0)
            {
                _logger.LogWarning("FCM send skipped for order {OrderId}: token list empty", orderId);
                return;
            }

            var title = "Đơn đã sẵn sàng";
            var body = $"Đơn #{orderId.ToString()[..8]} đã xong.";

            var link = string.IsNullOrWhiteSpace(_options.DefaultClickLink) ? "/" : _options.DefaultClickLink;

            var message = new MulticastMessage
            {
                Tokens = tokens,
                Webpush = new WebpushConfig
                {
                    Notification = new WebpushNotification
                    {
                        Title = title,
                        Body = body,
                    },
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = "order-ready",
                        ["orderId"] = orderId.ToString(),
                        ["pickupTime"] = pickupTime?.ToString("O") ?? "",
                        ["link"] = link,
                    }
                }
            };

            try
            {
                var messaging = FirebaseMessaging.GetMessaging(_app);
                var resp = await messaging.SendEachForMulticastAsync(message, cancellationToken);

                _logger.LogWarning(
                    "FCM send result for order {OrderId}: success={SuccessCount} failure={FailureCount} total={Total}",
                    orderId,
                    resp.SuccessCount,
                    resp.FailureCount,
                    tokens.Count);

                if (resp.FailureCount > 0)
                {
                    for (var i = 0; i < resp.Responses.Count; i++)
                    {
                        var r = resp.Responses[i];
                        if (r.IsSuccess) continue;

                        var tokenPrefix = tokens[i].Length > 12 ? tokens[i][..12] : tokens[i];
                        if (r.Exception is FirebaseMessagingException fme)
                        {
                            _logger.LogWarning(
                                "FCM failure idx={Idx} tokenPrefix={TokenPrefix} code={Code} message={Message}",
                                i,
                                tokenPrefix,
                                fme.MessagingErrorCode,
                                fme.Message);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "FCM failure idx={Idx} tokenPrefix={TokenPrefix} ex={ExceptionType} message={Message}",
                                i,
                                tokenPrefix,
                                r.Exception?.GetType().Name,
                                r.Exception?.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM send failed for order {OrderId}", orderId);
            }
        }
    }
}
