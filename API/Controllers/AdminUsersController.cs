using Application.DTOs.Users;
using Application.DTOs;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(Roles = "AdminSystem")]
    public class AdminUsersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdminUsersController(AppDbContext db)
        {
            _db = db;
        }

        // ===============================
        // CREATE USER
        // ===============================
        [HttpPost]
        public async Task<IActionResult> Create(CreateUserRequest request)
        {
            // 1️⃣ Validate role
            var role = await _db.Roles
                .FirstOrDefaultAsync(x => x.Name == request.Role);

            if (role == null)
                return BadRequest("Invalid role");

            // 2️⃣ Check email
            var exists = await _db.Users
                .AnyAsync(x => x.Email == request.Email);

            if (exists)
                return BadRequest("Email already exists");

            // 3️⃣ Create user (AUTO CAMPUS)
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                FullName = request.FullName,
                PasswordHash = PasswordHasher.Hash(request.Password),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);

            // 4️⃣ Gán role
            _db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });

            // 5️⃣ Nếu là Student thì tự tạo Wallet (balance = 0)
            if (string.Equals(role.Name, "Student", StringComparison.OrdinalIgnoreCase))
            {
                _db.Wallets.Add(new Wallet
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Balance = 0m,
                    Status = WalletStatus.Active,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                user.Id,
                user.Email,
                Role = role.Name
            });
        }

        // ===============================
        // LIST USERS
        // ===============================
        [HttpGet]
        public async Task<IActionResult> GetUsers(
        [FromQuery] string? role)
        {
            var query = _db.Users
                .AsQueryable();

            if (!string.IsNullOrEmpty(role))
            {
                query = query.Where(x =>
                    x.UserRoles.Any(ur => ur.Role.Name == role));
            }

            var users = await query
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.FullName,
                    x.IsActive,
                    Roles = x.UserRoles.Select(r => r.Role.Name)
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}
