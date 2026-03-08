namespace API.Services
{
    public class FcmOptions
    {
        // Preferred for deploy: store as secret/env var (Firebase__ServiceAccountJson)
        public string? ServiceAccountJson { get; set; }

        // Alternative: base64 encoded JSON (Firebase__ServiceAccountJsonBase64)
        public string? ServiceAccountJsonBase64 { get; set; }

        // Fallback for local dev: path to service account json file
        public string? ServiceAccountJsonPath { get; set; }
        public string? DefaultClickLink { get; set; }
    }
}
