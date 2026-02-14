using Core.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/manager/staff")]
    [Authorize(Roles = "Manager")]
    public class StaffRoleController : ControllerBase
    {
        private readonly AppDbContext _db;

        private static readonly string[] SecondaryStaffRoles =
        {
            "StaffKitchen",
            "StaffPOS",
            "StaffCoordination",
            "StaffDrink"
        };

        public StaffRoleController(AppDbContext db)
        {
            _db = db;
        }

        // =========================
        // LIST STAFF (for manager)
        // =========================
        [HttpGet("users")]
        public async Task<IActionResult> GetStaffUsers()
        {
            var users = await _db.Users
                .Where(x => !x.IsDeleted && x.UserRoles.Any(ur => ur.Role.Name == "Staff"))
                .OrderBy(x => x.FullName)
                .ThenBy(x => x.Email)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.FullName,
                    x.IsActive,
                    SecondaryRoles = x.UserRoles
                        .Select(ur => ur.Role.Name)
                        .Where(rn => SecondaryStaffRoles.Contains(rn))
                        .ToList()
                })
                .ToListAsync();

            return Ok(users);
        }

        // =========================
        // TOGGLE STAFF ACTIVE
        // =========================
        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> ToggleStaffActive(Guid id)
        {
            var user = await _db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (user == null)
                return NotFound();

            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (user.Id == currentUserId)
                return BadRequest("Cannot toggle yourself");

            if (user.UserRoles.All(ur => ur.Role.Name != "Staff"))
                return BadRequest("User is not Staff");

            if (user.UserRoles.Any(ur => ur.Role.Name == "AdminSystem" || ur.Role.Name == "Manager"))
                return BadRequest("Cannot toggle this user");

            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();

            return Ok(new { user.Id, user.IsActive });
        }

        // =========================
        // SOFT DELETE STAFF
        // =========================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteStaff(Guid id)
        {
            var user = await _db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (user == null)
                return NotFound();

            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (user.Id == currentUserId)
                return BadRequest("Cannot delete yourself");

            if (user.UserRoles.All(ur => ur.Role.Name != "Staff"))
                return BadRequest("User is not Staff");

            if (user.UserRoles.Any(ur => ur.Role.Name == "AdminSystem" || ur.Role.Name == "Manager"))
                return BadRequest("Cannot delete this user");

            user.IsDeleted = true;
            user.IsActive = false;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // =========================
        // GÁN ROLE PHỤ (kitchen | staffPos)
        // =========================
        [HttpPost("{userId}/assign")]
        public async Task<IActionResult> AssignRole(
            Guid userId,
            [FromQuery] string roleName)
        {
            if (roleName != "StaffKitchen" && roleName != "StaffPOS" && roleName != "StaffCoordination" && roleName != "StaffDrink")
                return BadRequest("Invalid staff role");

            var user = await _db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == userId);

            if (user == null)
                return NotFound();

            // ✔ phải là Staff
            var isStaff = user.UserRoles.Any(ur => ur.Role?.Name == "Staff");

            if (!isStaff)
                return BadRequest("User is not Staff");

            // Enforce: only ONE secondary role at a time.
            // Remove all other secondary roles before assigning the new one.
            var otherSecondary = user.UserRoles
                .Where(ur => ur.Role != null && SecondaryStaffRoles.Contains(ur.Role.Name) && ur.Role.Name != roleName)
                .ToList();

            if (otherSecondary.Count > 0)
            {
                _db.UserRoles.RemoveRange(otherSecondary);
            }

            var role = await _db.Roles
                .FirstOrDefaultAsync(x => x.Name == roleName);

            if (role == null)
                return BadRequest("Role not found");

            var exists = user.UserRoles.Any(x => x.RoleId == role.Id);
            if (!exists)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = role.Id
                });
            }

            await _db.SaveChangesAsync();
            return Ok("Role assigned");
        }

        // =========================
        // GỠ ROLE PHỤ
        // =========================
        [HttpDelete("{userId}/remove")]
        public async Task<IActionResult> RemoveRole(
            Guid userId,
            [FromQuery] string roleName)
        {
            if (roleName != "StaffKitchen" && roleName != "StaffPOS" && roleName != "StaffCoordination" && roleName != "StaffDrink")
                return BadRequest("Invalid staff role");

            var role = await _db.Roles
                .FirstOrDefaultAsync(x => x.Name == roleName);

            if (role == null)
                return BadRequest("Role not found");

            var userRole = await _db.UserRoles
                .Include(x => x.User)
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.RoleId == role.Id);

            if (userRole == null)
                return NotFound();

            _db.UserRoles.Remove(userRole);
            await _db.SaveChangesAsync();

            return Ok("Role removed");
        }
    }
}
