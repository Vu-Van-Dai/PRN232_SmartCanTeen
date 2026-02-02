using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace API.Controllers
{
    [ApiController]
    [Route("api/uploads")]
    [Authorize]
    public class UploadsController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;

        public UploadsController(IConfiguration config, AppDbContext db)
        {
            _config = config;
            _db = db;
        }

        public class CloudinarySignatureRequest
        {
            public string? Folder { get; set; }
        }

        [HttpPost("cloudinary-signature")]
        public async Task<IActionResult> CreateCloudinarySignature([FromBody] CloudinarySignatureRequest request)
        {
            // Require a real user
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var userId))
                return Unauthorized();

            var userExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == userId);
            if (!userExists)
                return Unauthorized();

            var cloudName = _config["Cloudinary:CloudName"];
            var apiKey = _config["Cloudinary:ApiKey"];
            var apiSecret = _config["Cloudinary:ApiSecret"];

            if (string.IsNullOrWhiteSpace(cloudName) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                return Problem("Cloudinary is not configured", statusCode: 500);

            // Force folder to a safe namespace; allow optional subfolder.
            var folder = string.IsNullOrWhiteSpace(request?.Folder)
                ? "smartcanteen/avatars"
                : request.Folder.Trim();

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Cloudinary signature: sha1("folder=...&timestamp=..." + api_secret)
            var toSign = $"folder={folder}&timestamp={timestamp}";
            var signature = Sha1Hex(toSign + apiSecret);

            return Ok(new
            {
                cloudName,
                apiKey,
                timestamp,
                folder,
                signature
            });
        }

        private static string Sha1Hex(string input)
        {
            using var sha1 = SHA1.Create();
            var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
