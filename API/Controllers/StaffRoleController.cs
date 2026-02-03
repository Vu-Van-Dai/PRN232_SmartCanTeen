using Core.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/manager/staff")]
    [Authorize(Roles = "Manager")]
    public class StaffRoleController : ControllerBase
    {
        private readonly AppDbContext _db;

        public StaffRoleController(AppDbContext db)
        {
            _db = db;
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
                .FirstOrDefaultAsync(x => x.Id == userId);

            if (user == null)
                return NotFound();

            // ✔ phải là Staff
            var isStaff = user.UserRoles.Any(ur =>
                _db.Roles.Any(r =>
                    r.Id == ur.RoleId && r.Name == "Staff"));

            if (!isStaff)
                return BadRequest("User is not Staff");

            var role = await _db.Roles
                .FirstOrDefaultAsync(x => x.Name == roleName);

            if (role == null)
                return BadRequest("Role not found");

            var exists = user.UserRoles.Any(x => x.RoleId == role.Id);
            if (exists)
                return BadRequest("User already has this role");

            _db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });

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
