using Application.DTOs;
using Application.DTOs.Users;
using Application.JWTToken;
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
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly JwtTokenService _jwt;
        private readonly IEmailSender _email;

        public AuthController(AppDbContext db, JwtTokenService jwt, IEmailSender email)
        {
            _db = db;
            _jwt = jwt;
            _email = email;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var user = await _db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Email == request.Email);

            if (user == null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
                return Unauthorized("Invalid credentials");

            if (!user.IsActive)
                return Unauthorized("Account is locked");

            var roles = user.UserRoles.Select(r => r.Role.Name);

            var token = _jwt.GenerateToken(
                userId: user.Id,
                roles: roles,
                email: user.Email,
                name: user.FullName ?? user.Email,
                mustChangePassword: user.MustChangePassword
            );

            var expiredAt = DateTime.UtcNow.AddMinutes(_jwt.GetExpireMinutes());

            return Ok(new LoginResponse
            {
                AccessToken = token,
                ExpiredAt = expiredAt
            });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest("Missing password");

            if (request.NewPassword == request.CurrentPassword)
                return BadRequest("New password must be different");

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
                return BadRequest("Current password is incorrect");

            user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();

            // Re-issue token so the must-change claim is cleared.
            var roles = await _db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.Role.Name)
                .ToListAsync();

            var token = _jwt.GenerateToken(
                userId: user.Id,
                roles: roles,
                email: user.Email,
                name: user.FullName ?? user.Email,
                mustChangePassword: user.MustChangePassword
            );

            return Ok(new LoginResponse
            {
                AccessToken = token,
                ExpiredAt = DateTime.UtcNow.AddMinutes(_jwt.GetExpireMinutes())
            });
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
        {
            var email = (request.Email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Missing email");

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email);

            // Always return OK to reduce account enumeration.
            if (user == null)
                return Ok(new { message = "If the email exists, an OTP has been sent." });

            var otp = GenerateNumericOtp(6);
            var entity = new PasswordResetOtp
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CodeHash = PasswordHasher.Hash(otp),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTime.UtcNow
            };

            _db.PasswordResetOtps.Add(entity);
            await _db.SaveChangesAsync();

            var subject = "Mã OTP đặt lại mật khẩu SmartCanteen";
            var html = $@"
<p>Xin chào,</p>
<p>Bạn đã yêu cầu đặt lại mật khẩu. Mã OTP của bạn là:</p>
<h2 style='letter-spacing:2px'>{otp}</h2>
<p>Mã OTP có hiệu lực trong <b>10 phút</b>.</p>
<p>Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>";

            await _email.SendAsync(user.Email, subject, html);

            return Ok(new { message = "If the email exists, an OTP has been sent." });
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPasswordWithOtp(ResetPasswordWithOtpRequest request)
        {
            var email = (request.Email ?? string.Empty).Trim();
            var otp = (request.Otp ?? string.Empty).Trim();
            var newPassword = request.NewPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp) || string.IsNullOrWhiteSpace(newPassword))
                return BadRequest("Missing fields");

            if (newPassword.Length < 6)
                return BadRequest("Password too short");

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email);
            if (user == null)
                return BadRequest("Invalid OTP");

            var now = DateTime.UtcNow;
            var otpEntity = await _db.PasswordResetOtps
                .Where(x => x.UserId == user.Id && x.ConsumedAt == null && x.ExpiresAt > now)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (otpEntity == null)
                return BadRequest("Invalid OTP");

            otpEntity.Attempts += 1;
            if (otpEntity.Attempts > 10)
            {
                otpEntity.ConsumedAt = now;
                await _db.SaveChangesAsync();
                return BadRequest("Invalid OTP");
            }

            if (!PasswordHasher.Verify(otp, otpEntity.CodeHash))
            {
                await _db.SaveChangesAsync();
                return BadRequest("Invalid OTP");
            }

            otpEntity.ConsumedAt = now;
            user.PasswordHash = PasswordHasher.Hash(newPassword);
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password reset successfully" });
        }

        private static string GenerateNumericOtp(int length)
        {
            // cryptographically-strong OTP
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);

            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)('0' + (bytes[i] % 10));
            }
            return new string(chars);
        }
    }
}
