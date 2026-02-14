using Application.DTOs.Users;
using Application.DTOs;
using API.Services.Email;
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
        private readonly IEmailSender _email;

        public AdminUsersController(AppDbContext db, IEmailSender email)
        {
            _db = db;
            _email = email;
        }

        // ===============================
        // CREATE USER
        // ===============================
        [HttpPost]
        public async Task<IActionResult> Create(CreateUserRequest request)
        {
            var email = (request.Email ?? string.Empty).Trim();
            var fullName = (request.FullName ?? string.Empty).Trim();
            var studentCode = (request.StudentCode ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
                return BadRequest("Missing fields");

            // Admin chỉ tạo được role chính; không tạo được role phụ của Staff
            var requestedRole = (request.Role ?? string.Empty).Trim();
            var disallowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AdminSystem",
                "StaffPOS",
                "StaffKitchen",
                "StaffCoordination",
                "StaffDrink"
            };

            var allowedMain = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Student",
                "Parent",
                "Staff",
                "Manager"
            };

            if (string.IsNullOrWhiteSpace(requestedRole) || disallowed.Contains(requestedRole) || !allowedMain.Contains(requestedRole))
                return BadRequest("Invalid role");

            // StudentCode is required for Student accounts
            if (string.Equals(requestedRole, "Student", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(studentCode))
                    return BadRequest("Missing studentCode");

                var studentCodeLower = studentCode.ToLower();

                var codeExists = await _db.Users
                    .AnyAsync(x => x.StudentCode != null && x.StudentCode.ToLower() == studentCodeLower);

                if (codeExists)
                    return BadRequest("StudentCode already exists");
            }

            // 1️⃣ Validate role
            var role = await _db.Roles
                .FirstOrDefaultAsync(x => x.Name == requestedRole);

            if (role == null)
                return BadRequest("Invalid role");

            // 2️⃣ Check email
            var exists = await _db.Users
                .AnyAsync(x => x.Email == email);

            if (exists)
                return BadRequest("Email already exists");

            // 3️⃣ Create user (AUTO CAMPUS)
            var oneTimePassword = string.IsNullOrWhiteSpace(request.Password)
                ? GenerateTemporaryPassword(10)
                : request.Password;

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                FullName = fullName,
                StudentCode = string.IsNullOrWhiteSpace(studentCode) ? null : studentCode,
                PasswordHash = PasswordHasher.Hash(oneTimePassword!),
                MustChangePassword = true,
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

            // 6️⃣ Send email with credentials
            var subject = "Tài khoản SmartCanteen của bạn";
            var html = $@"
<p>Xin chào <b>{System.Net.WebUtility.HtmlEncode(user.FullName ?? user.Email)}</b>,</p>
<p>Tài khoản của bạn đã được tạo trên hệ thống SmartCanteen.</p>
<p><b>Tên đăng nhập:</b> {System.Net.WebUtility.HtmlEncode(user.Email)}<br/>
<b>Mật khẩu tạm thời:</b> {System.Net.WebUtility.HtmlEncode(oneTimePassword!)}</p>
<p>Khi đăng nhập lần đầu, bạn sẽ được yêu cầu <b>đổi mật khẩu</b>.</p>";

            await _email.SendAsync(user.Email, subject, html);

            return Ok(new
            {
                user.Id,
                user.Email,
                Role = role.Name
            });
        }

        [HttpPost("{id}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id)
        {
            var user = await _db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (user == null)
                return NotFound();

            // ❌ Do not reset yourself
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (user.Id == currentUserId)
                return BadRequest("Cannot reset yourself");

            // ❌ Do not reset AdminSystem
            if (user.UserRoles.Any(ur => string.Equals(ur.Role.Name, "AdminSystem", StringComparison.OrdinalIgnoreCase)))
                return BadRequest("Cannot reset AdminSystem");

            var oneTimePassword = GenerateTemporaryPassword(10);
            user.PasswordHash = PasswordHasher.Hash(oneTimePassword);
            user.MustChangePassword = true;
            await _db.SaveChangesAsync();

            var subject = "Đặt lại mật khẩu SmartCanteen";
            var html = $@"
<p>Xin chào <b>{System.Net.WebUtility.HtmlEncode(user.FullName ?? user.Email)}</b>,</p>
<p>Quản trị viên đã đặt lại mật khẩu của bạn.</p>
<p><b>Tên đăng nhập:</b> {System.Net.WebUtility.HtmlEncode(user.Email)}<br/>
<b>Mật khẩu tạm thời:</b> {System.Net.WebUtility.HtmlEncode(oneTimePassword)}</p>
<p>Khi đăng nhập, bạn sẽ được yêu cầu <b>đổi mật khẩu</b>.</p>";

            await _email.SendAsync(user.Email, subject, html);

            return Ok(new { message = "Password reset email sent" });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
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

            if (user.UserRoles.Any(ur => string.Equals(ur.Role.Name, "AdminSystem", StringComparison.OrdinalIgnoreCase)))
                return BadRequest("Cannot delete AdminSystem");

            user.IsDeleted = true;
            user.IsActive = false;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private static string GenerateTemporaryPassword(int length)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);

            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            }
            return new string(chars);
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
