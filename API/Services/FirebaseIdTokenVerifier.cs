using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace API.Services
{
    public class FirebaseIdTokenVerifier
    {
        private readonly ILogger<FirebaseIdTokenVerifier> _logger;
        private readonly FcmOptions _options;
        private readonly IHostEnvironment _env;

        private FirebaseApp? _app;
        private static bool _initialized;

        public FirebaseIdTokenVerifier(
            ILogger<FirebaseIdTokenVerifier> logger,
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
                var existing = FirebaseApp.DefaultInstance;
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
                        "Firebase Auth verification disabled: configure Firebase:ServiceAccountJson (recommended) or Firebase:ServiceAccountJsonBase64, or Firebase:ServiceAccountJsonPath.");
                    return false;
                }

                if (!File.Exists(path))
                {
                    _logger.LogWarning("Firebase Auth verification disabled: service account file not found at {Path}", path);
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
                    var existing = FirebaseApp.DefaultInstance;
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
                    GoogleCredential credential;
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        credential = GoogleCredential.FromJson(json);
                    }
                    else
                    {
                        credential = GoogleCredential.FromFile(path!);
                    }

                    _app = FirebaseApp.Create(new AppOptions { Credential = credential });

                    _initialized = true;
                    _logger.LogInformation("Firebase Admin initialized successfully for Auth verification.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize FirebaseApp for Auth verification.");
                    return false;
                }
            }
        }

        public async Task<FirebaseToken?> VerifyAsync(string firebaseIdToken, CancellationToken cancellationToken = default)
        {
            var token = (firebaseIdToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token)) return null;

            if (!EnsureInitialized() || _app == null) return null;

            try
            {
                var auth = FirebaseAuth.GetAuth(_app);
                return await auth.VerifyIdTokenAsync(token, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Firebase token verification failed.");
                return null;
            }
        }
    }
}
