using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Projex_backend.Data;
using Projex_backend.Dtos;
using Projex_backend.Helpers;
using Projex_backend.Models;
using Projex_backend.Security;
using Projex_backend.Services;

namespace Projex_backend.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IJwtService _jwtService;
        private readonly IEmailSender _emailSender;

        public AuthController(AppDbContext db, IConfiguration config, IJwtService jwtService, IEmailSender emailSender)
        {
            _db = db;
            _config = config;
            _jwtService = jwtService;
            _emailSender = emailSender;
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (_db.Users.Any(x => x.Email == request.Email))
            {
                return BadRequest(new { message = "Email already exists." });
            }

            var user = new User
            {
                Email = request.Email.Trim(),
                FullName = request.FullName.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _db.Users.Add(user);
            _db.SaveChanges();

            return Ok(new { message = "Register successfully." });
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var user = _db.Users.FirstOrDefault(x => x.Email == request.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            if (!user.IsActive)
            {
                return Unauthorized(new { message = "Account is inactive." });
            }

            var expireDays = _config.GetValue<int?>("Jwt:ExpireDays") ?? 7;
            var token = _jwtService.GenerateToken(user, expireDays);

            return Ok(new
            {
                token,
                expiresAt = DateTime.UtcNow.AddDays(expireDays),
                user = new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.PhoneNumber,
                    user.AvatarUrl
                }
            });
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var userId = User.GetUserId();
            var user = _db.Users.Find(userId);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            return Ok(new
            {
                user.Id,
                user.Email,
                user.FullName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.IsActive,
                user.CreatedAt
            });
        }

        [Authorize]
        [HttpPut("profile")]
        public IActionResult UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();
            var user = _db.Users.Find(userId);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (_db.Users.Any(x => x.Email == request.Email && x.Id != userId))
            {
                return BadRequest(new { message = "Email already exists." });
            }

            user.Email = request.Email.Trim();
            user.FullName = request.FullName.Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            user.UpdatedAt = DateTime.UtcNow;

            _db.SaveChanges();

            return Ok(new
            {
                message = "Profile updated successfully.",
                user = new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.PhoneNumber,
                    user.AvatarUrl
                }
            });
        }

        [Authorize]
        [HttpPatch("avatar")]
        public IActionResult UpdateAvatar([FromBody] UpdateAvatarRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();
            var user = _db.Users.Find(userId);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            user.AvatarUrl = request.AvatarUrl.Trim();
            user.UpdatedAt = DateTime.UtcNow;
            _db.SaveChanges();

            return Ok(new
            {
                message = "Avatar updated successfully.",
                avatarUrl = user.AvatarUrl
            });
        }

        [Authorize]
        [HttpPost("change-password")]
        public IActionResult ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();
            var user = _db.Users.Find(userId);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new { message = "Current password is incorrect." });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            _db.SaveChanges();

            return Ok(new { message = "Password changed successfully." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var email = request.Email.Trim();
            var user = _db.Users.FirstOrDefault(x => x.Email == email);
            if (user == null)
            {
                return NotFound(new { message = "Email does not exist." });
            }

            var activeTokens = _db.PasswordResetTokens
                .Where(x => x.UserId == user.Id && !x.IsUsed)
                .ToList();

            foreach (var token in activeTokens)
            {
                token.IsUsed = true;
            }

            var code = Random.Shared.Next(1000, 10000).ToString();
            var resetToken = new PasswordResetToken
            {
                UserId = user.Id,
                Code = code,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.PasswordResetTokens.Add(resetToken);
            _db.SaveChanges();

            try
            {
                await _emailSender.SendPasswordResetCodeAsync(email, code, resetToken.ExpiresAt);
            }
            catch (Exception ex)
            {
                resetToken.IsUsed = true;
                _db.SaveChanges();

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Could not send reset code email.",
                    detail = ex.Message
                });
            }

            return Ok(new
            {
                message = "Reset code sent to email successfully.",
                expiresAt = resetToken.ExpiresAt
            });
        }

        [HttpPost("verify-reset-code")]
        public IActionResult VerifyResetCode([FromBody] VerifyResetCodeRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var token = GetValidResetToken(request.Email, request.Code);
            if (token == null)
            {
                return BadRequest(new { message = "Invalid or expired reset code." });
            }

            return Ok(new
            {
                message = "Reset code is valid.",
                expiresAt = token.ExpiresAt
            });
        }

        [HttpPost("reset-password")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var token = GetValidResetToken(request.Email, request.Code);
            if (token == null)
            {
                return BadRequest(new { message = "Invalid or expired reset code." });
            }

            var user = _db.Users.Find(token.UserId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            token.IsUsed = true;
            _db.SaveChanges();

            return Ok(new { message = "Password reset successfully." });
        }

        private PasswordResetToken? GetValidResetToken(string email, string code)
        {
            email = email.Trim();
            code = code.Trim();

            return _db.PasswordResetTokens
                .Where(x =>
                    x.User.Email == email &&
                    x.Code == code &&
                    !x.IsUsed &&
                    x.ExpiresAt >= DateTime.UtcNow)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();
        }
    }
}
