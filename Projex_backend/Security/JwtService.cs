using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Projex_backend.Models;

namespace Projex_backend.Security
{
    public interface IJwtService
    {
        string GenerateToken(User user, int expireDays);
        ClaimsPrincipal? ValidateToken(string token);
    }

    public class JwtService : IJwtService
    {
        private readonly IConfiguration _configuration;

        public JwtService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateToken(User user, int expireDays)
        {
            var claims = new[]
            {
                new Claim("UserId", user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            // SigningCredentials tells the JWT library which key and algorithm to use
            // when creating the signature part of header.payload.signature.
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // JwtSecurityToken builds the token payload and standard JWT metadata
            // such as iss, aud, nbf and exp before it is serialized.
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddDays(expireDays),
                signingCredentials: credentials);

            // WriteToken serializes the token object into the final JWT string.
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidAudience = _configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!)),
                ClockSkew = TimeSpan.Zero,
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            };

            try
            {
                // ValidateToken parses the JWT, rebuilds and verifies the signature,
                // then checks exp, nbf, iss and aud based on TokenValidationParameters.
                return tokenHandler.ValidateToken(token, validationParameters, out _);
            }
            catch
            {
                return null;
            }
        }
    }
}
