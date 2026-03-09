using Application.DTOs.Users;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Core.Entities;

namespace API.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersMeController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<UsersMeController> _logger;

        public UsersMeController(AppDbContext db, ILogger<UsersMeController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var userId))
                return Unauthorized();

            var user = await _db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == userId);

            if (user == null)
                return NotFound();

            return Ok(new MeProfileResponse
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                StudentCode = user.StudentCode,
                AvatarUrl = user.AvatarUrl,
                OrderReadyNotificationsEnabled = user.OrderReadyNotificationsEnabled,
                Roles = user.UserRoles.Select(x => x.Role.Name).ToList()
            });
        }

        [HttpPatch("me")]
        public async Task<IActionResult> PatchMe(UpdateMeProfileRequest request)
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var userId))
                return Unauthorized();

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (request.AvatarUrl != null)
                user.AvatarUrl = request.AvatarUrl;

            if (request.OrderReadyNotificationsEnabled.HasValue)
                user.OrderReadyNotificationsEnabled = request.OrderReadyNotificationsEnabled.Value;

            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("me/fcm-token")]
        public async Task<IActionResult> RegisterFcmToken(RegisterFcmTokenRequest request)
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var userId))
                return Unauthorized();

            var token = (request.Token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("token is required");

            _logger.LogInformation(
                "RegisterFcmToken: upsert token for user {UserId}, tokenPrefix={TokenPrefix}",
                userId,
                token.Length > 12 ? token[..12] : token);

            var now = DateTime.UtcNow;
            var id = Guid.NewGuid();

            // Race-safe idempotent write: avoids duplicate key on (UserId, Token)
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO "UserFcmTokens" ("Id", "UserId", "Token", "CreatedAt", "LastSeenAt", "IsActive")
VALUES ({id}, {userId}, {token}, {now}, {now}, TRUE)
ON CONFLICT ("UserId", "Token")
DO UPDATE SET
    "LastSeenAt" = EXCLUDED."LastSeenAt",
    "IsActive" = TRUE;
""");

            return NoContent();
        }
    }
}
