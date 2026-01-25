using Application.DTOs;
using Application.DTOs.Users;
using Application.JWTToken;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly JwtTokenService _jwt;

        public AuthController(AppDbContext db, JwtTokenService jwt)
        {
            _db = db;
            _jwt = jwt;
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

            var token = _jwt.GenerateToken(user.Id, roles);

            return Ok(new LoginResponse
            {
                AccessToken = token,
                ExpiredAt = DateTime.UtcNow.AddMinutes(120)
            });
        }
    }
}
