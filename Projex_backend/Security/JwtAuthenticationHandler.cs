using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Projex_backend.Security
{
    public class JwtAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IJwtService _jwtService;

        public JwtAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IJwtService jwtService)
            : base(options, logger, encoder)
        {
            _jwtService = jwtService;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var headerValue = authorizationHeader.ToString();
            if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var token = headerValue["Bearer ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return Task.FromResult(AuthenticateResult.Fail("Token is missing."));
            }

            var principal = _jwtService.ValidateToken(token);
            if (principal == null)
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token."));
            }

            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
