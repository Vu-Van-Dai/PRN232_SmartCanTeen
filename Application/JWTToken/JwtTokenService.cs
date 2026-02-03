using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Application.JWTToken
{
    public class JwtTokenService
    {
        private readonly IConfiguration _config;

        public const string MustChangePasswordClaim = "must_change_pwd";

        public JwtTokenService(IConfiguration config)
        {
            _config = config;
        }

        public int GetExpireMinutes()
        {
            if (int.TryParse(_config["Jwt:ExpireMinutes"], out var minutes) && minutes > 0)
                return minutes;
            return 120;
        }

        public string GenerateToken(Guid userId, IEnumerable<string> roles)
        {
            return GenerateToken(userId, roles, email: null, name: null, mustChangePassword: false);
        }

        public string GenerateToken(Guid userId, IEnumerable<string> roles, string? email, string? name, bool mustChangePassword)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new(ClaimTypes.NameIdentifier, userId.ToString())
            };

            if (!string.IsNullOrWhiteSpace(email))
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
                claims.Add(new Claim("email", email));
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                claims.Add(new Claim(ClaimTypes.Name, name));
                claims.Add(new Claim("name", name));
            }

            if (mustChangePassword)
            {
                claims.Add(new Claim(MustChangePasswordClaim, "1"));
            }

            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)
            );

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(GetExpireMinutes()),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
