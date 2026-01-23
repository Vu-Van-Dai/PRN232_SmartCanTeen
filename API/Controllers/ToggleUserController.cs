using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(Roles = "AdminSystem,AdminCampus")]
    public class ToggleUserController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentCampusService _campus;

        public ToggleUserController(
            AppDbContext db,
            ICurrentCampusService campus)
        {
            _db = db;
            _campus = campus;
        }

        // ============================
        // TOGGLE USER ACTIVE
        // ============================
        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> Toggle(Guid id)
        {
            var user = await _db.Users
                .Include(x => x.UserRoles)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (user == null)
                return NotFound();

            // ❌ Không cho tự khóa chính mình
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (user.Id == currentUserId)
                return BadRequest("Cannot toggle yourself");

            // ❌ AdminCampus chỉ được toggle user cùng campus
            if (User.IsInRole("AdminCampus") &&
                user.CampusId != _campus.CampusId)
            {
                return Forbid();
            }

            // ❌ Không cho khóa AdminSystem
            var isAdminSystem = user.UserRoles.Any(r =>
                _db.Roles.Any(role =>
                    role.Id == r.RoleId &&
                    role.Name == "AdminSystem"));

            if (isAdminSystem)
                return BadRequest("Cannot toggle AdminSystem");

            user.IsActive = !user.IsActive;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                user.Id,
                user.IsActive
            });
        }
    }

}
