using Application.DTOs.Users;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersMeController : ControllerBase
    {
        private readonly AppDbContext _db;

        public UsersMeController(AppDbContext db)
        {
            _db = db;
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
    }
}
