using System.Security.Claims;

namespace Projex_backend.Helpers
{
    public static class ClaimsPrincipalExtensions
    {
        public static int GetUserId(this ClaimsPrincipal user)
        {
            var value = user.FindFirst("UserId")?.Value;

            if (!int.TryParse(value, out var userId))
            {
                throw new UnauthorizedAccessException("User claim is invalid.");
            }

            return userId;
        }
    }
}
