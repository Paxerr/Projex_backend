using System.Text;
using System.Text.Json.Serialization;
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
        private readonly IHttpClientFactory _httpClientFactory;

        public AuthController(
            AppDbContext db,
            IConfiguration config,
            IJwtService jwtService,
            IEmailSender emailSender,
            IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _config = config;
            _jwtService = jwtService;
            _emailSender = emailSender;
            _httpClientFactory = httpClientFactory;
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
                CreatedAt = DateTime.Now,
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
                expiresAt = DateTime.Now.AddDays(expireDays),
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

        [HttpGet("google")]
        public IActionResult GoogleLogin([FromQuery(Name = "redirect_uri")] string? mobileRedirectUri)
        {
            return StartOAuth("Google", "https://accounts.google.com/o/oauth2/v2/auth", mobileRedirectUri, "openid email profile");
        }

        [HttpGet("github")]
        public IActionResult GitHubLogin([FromQuery(Name = "redirect_uri")] string? mobileRedirectUri)
        {
            return StartOAuth("GitHub", "https://github.com/login/oauth/authorize", mobileRedirectUri, "read:user user:email");
        }

        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return RedirectToMobile(state, ("error", error));
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return RedirectToMobile(state, ("error", "missing_code"));
            }

            var options = GetOAuthOptions("Google");
            if (!options.IsConfigured)
            {
                return RedirectToMobile(state, ("error", "google_oauth_not_configured"));
            }

            var callbackUrl = BuildBackendCallbackUrl("google");
            var http = _httpClientFactory.CreateClient();
            var tokenResponse = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = callbackUrl
            }));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                return RedirectToMobile(state, ("error", "google_token_exchange_failed"));
            }

            var token = await tokenResponse.Content.ReadFromJsonAsync<OAuthTokenResponse>();
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                return RedirectToMobile(state, ("error", "google_access_token_missing"));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

            var userInfoResponse = await http.SendAsync(request);
            if (!userInfoResponse.IsSuccessStatusCode)
            {
                return RedirectToMobile(state, ("error", "google_userinfo_failed"));
            }

            var googleUser = await userInfoResponse.Content.ReadFromJsonAsync<GoogleUserInfo>();
            if (googleUser == null || string.IsNullOrWhiteSpace(googleUser.Email))
            {
                return RedirectToMobile(state, ("error", "google_email_missing"));
            }

            var user = UpsertOAuthUser(googleUser.Email, googleUser.Name, googleUser.Picture);
            return RedirectToMobileWithToken(state, user);
        }

        [HttpGet("github/callback")]
        public async Task<IActionResult> GitHubCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return RedirectToMobile(state, ("error", error));
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return RedirectToMobile(state, ("error", "missing_code"));
            }

            var options = GetOAuthOptions("GitHub");
            if (!options.IsConfigured)
            {
                return RedirectToMobile(state, ("error", "github_oauth_not_configured"));
            }

            var callbackUrl = BuildBackendCallbackUrl("github");
            var http = _httpClientFactory.CreateClient();
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.ClientId!,
                    ["client_secret"] = options.ClientSecret!,
                    ["code"] = code,
                    ["redirect_uri"] = callbackUrl
                })
            };
            tokenRequest.Headers.Accept.ParseAdd("application/json");

            var tokenResponse = await http.SendAsync(tokenRequest);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return RedirectToMobile(state, ("error", "github_token_exchange_failed"));
            }

            var token = await tokenResponse.Content.ReadFromJsonAsync<OAuthTokenResponse>();
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                return RedirectToMobile(state, ("error", "github_access_token_missing"));
            }

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
            userRequest.Headers.UserAgent.ParseAdd("ProjexBackend/1.0");

            var userInfoResponse = await http.SendAsync(userRequest);
            if (!userInfoResponse.IsSuccessStatusCode)
            {
                return RedirectToMobile(state, ("error", "github_userinfo_failed"));
            }

            var githubUser = await userInfoResponse.Content.ReadFromJsonAsync<GitHubUserInfo>();
            var email = githubUser?.Email;

            if (string.IsNullOrWhiteSpace(email))
            {
                email = await GetPrimaryGitHubEmail(http, token.AccessToken);
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToMobile(state, ("error", "github_email_missing"));
            }

            var displayName = FirstNonEmpty(githubUser?.Name, githubUser?.Login, email);
            var user = UpsertOAuthUser(email, displayName, githubUser?.AvatarUrl);
            return RedirectToMobileWithToken(state, user);
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
            user.UpdatedAt = DateTime.Now;

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
            user.UpdatedAt = DateTime.Now;

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
                ExpiresAt = DateTime.Now.AddMinutes(10),
                IsUsed = false,
                CreatedAt = DateTime.Now
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
            user.UpdatedAt = DateTime.Now;
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
                    x.ExpiresAt >= DateTime.Now)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();
        }

        private IActionResult StartOAuth(string provider, string authorizeUrl, string? mobileRedirectUri, string scope)
        {
            if (!IsAllowedMobileRedirectUri(mobileRedirectUri))
            {
                return BadRequest(new { message = "Invalid redirect_uri." });
            }

            var options = GetOAuthOptions(provider);
            if (!options.IsConfigured)
            {
                return BadRequest(new { message = $"{provider} OAuth is not configured." });
            }

            var callbackUrl = BuildBackendCallbackUrl(provider.ToLowerInvariant());
            var state = EncodeState(mobileRedirectUri!);
            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId!,
                ["redirect_uri"] = callbackUrl,
                ["response_type"] = "code",
                ["scope"] = scope,
                ["state"] = state
            };

            if (provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
            {
                parameters["access_type"] = "online";
                parameters["prompt"] = "select_account";
            }

            return Redirect(BuildUrl(authorizeUrl, parameters));
        }

        private User UpsertOAuthUser(string email, string? fullName, string? avatarUrl)
        {
            email = email.Trim();
            var user = _db.Users.FirstOrDefault(x => x.Email == email);

            if (user == null)
            {
                user = new User
                {
                    Email = email,
                    FullName = FirstNonEmpty(fullName, email.Split('@')[0])!,
                    AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                    CreatedAt = DateTime.Now,
                    IsActive = true
                };

                _db.Users.Add(user);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(fullName))
                {
                    user.FullName = fullName.Trim();
                }

                if (string.IsNullOrWhiteSpace(user.AvatarUrl) && !string.IsNullOrWhiteSpace(avatarUrl))
                {
                    user.AvatarUrl = avatarUrl.Trim();
                }

                user.UpdatedAt = DateTime.Now;
            }

            _db.SaveChanges();
            return user;
        }

        private IActionResult RedirectToMobileWithToken(string? state, User user)
        {
            if (!user.IsActive)
            {
                return RedirectToMobile(state, ("error", "account_inactive"));
            }

            var expireDays = _config.GetValue<int?>("Jwt:ExpireDays") ?? 7;
            var token = _jwtService.GenerateToken(user, expireDays);

            return RedirectToMobile(
                state,
                ("token", token),
                ("email", user.Email),
                ("name", user.FullName),
                ("userId", user.Id.ToString()));
        }

        private IActionResult RedirectToMobile(string? state, params (string Key, string? Value)[] parameters)
        {
            var mobileRedirectUri = DecodeState(state);
            if (!IsAllowedMobileRedirectUri(mobileRedirectUri))
            {
                return BadRequest(new { message = "Invalid OAuth state." });
            }

            return Redirect(BuildUrl(mobileRedirectUri!, parameters
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => x.Key, x => x.Value!)));
        }

        private async Task<string?> GetPrimaryGitHubEmail(HttpClient http, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.ParseAdd("ProjexBackend/1.0");

            var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var emails = await response.Content.ReadFromJsonAsync<List<GitHubEmailInfo>>();
            return emails?
                .Where(x => x.Verified)
                .OrderByDescending(x => x.Primary)
                .Select(x => x.Email)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }

        private OAuthOptions GetOAuthOptions(string provider)
        {
            return new OAuthOptions
            {
                ClientId = _config[$"OAuth:{provider}:ClientId"],
                ClientSecret = _config[$"OAuth:{provider}:ClientSecret"]
            };
        }

        private string BuildBackendCallbackUrl(string provider)
        {
            var baseUrl = _config["OAuth:BackendBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = $"{Request.Scheme}://{Request.Host}";
            }

            return $"{baseUrl.TrimEnd('/')}/api/auth/{provider}/callback";
        }

        private static bool IsAllowedMobileRedirectUri(string? redirectUri)
        {
            return Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
                   && uri.Scheme == "projex"
                   && uri.Host == "auth"
                   && uri.AbsolutePath == "/callback";
        }

        private static string EncodeState(string mobileRedirectUri)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(mobileRedirectUri));
        }

        private static string? DecodeState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(state));
            }
            catch
            {
                return null;
            }
        }

        private static string BuildUrl(string baseUrl, IReadOnlyDictionary<string, string> parameters)
        {
            var query = string.Join("&", parameters.Select(x =>
                $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

            return baseUrl.Contains('?') ? $"{baseUrl}&{query}" : $"{baseUrl}?{query}";
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
        }

        private sealed class OAuthOptions
        {
            public string? ClientId { get; init; }
            public string? ClientSecret { get; init; }
            public bool IsConfigured =>
                !string.IsNullOrWhiteSpace(ClientId)
                && !string.IsNullOrWhiteSpace(ClientSecret)
                && !ClientId.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
                && !ClientSecret.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class OAuthTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; init; }
        }

        private sealed class GoogleUserInfo
        {
            [JsonPropertyName("email")]
            public string? Email { get; init; }

            [JsonPropertyName("name")]
            public string? Name { get; init; }

            [JsonPropertyName("picture")]
            public string? Picture { get; init; }
        }

        private sealed class GitHubUserInfo
        {
            [JsonPropertyName("login")]
            public string? Login { get; init; }

            [JsonPropertyName("name")]
            public string? Name { get; init; }

            [JsonPropertyName("email")]
            public string? Email { get; init; }

            [JsonPropertyName("avatar_url")]
            public string? AvatarUrl { get; init; }
        }

        private sealed class GitHubEmailInfo
        {
            [JsonPropertyName("email")]
            public string? Email { get; init; }

            [JsonPropertyName("primary")]
            public bool Primary { get; init; }

            [JsonPropertyName("verified")]
            public bool Verified { get; init; }
        }
    }
}
