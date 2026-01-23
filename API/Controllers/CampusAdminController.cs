using Core.Entities;
using Infrastructure.Persistence;
using Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/campus-admin")]
    [Authorize(Roles = "SystemAdmin")]
    public class CampusAdminController : ControllerBase
    {
        private readonly AppDbContext _db;

        public CampusAdminController(AppDbContext db)
        {
            _db = db;
        }

        // ============================
        // TẠO CAMPUS
        // ============================
        [HttpPost("campuses")]
        public async Task<IActionResult> CreateCampus(string name)
        {
            var campus = new Campus
            {
                Id = Guid.NewGuid(),
                Name = name
            };

            _db.Campuses.Add(campus);
            await _db.SaveChangesAsync();

            return Ok(campus);
        }

        // ============================
        // TẠO ADMIN TRƯỜNG
        // ============================
        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdmin(
            string email,
            string password,
            Guid campusId)
        {
            var role = await _db.Roles.FirstAsync(x => x.Name == "Admin");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                CampusId = campusId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);

            _db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });

            await _db.SaveChangesAsync();

            return Ok();
        }
    }
}
