using System.Security.Claims;

namespace Localll.Common.Auth;

public static class CurrentUser
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub")
                    ?? throw new UnauthorizedAccessException("Token does not contain a user id claim.");
        return Guid.Parse(value);
    }

    public static string? GetRole(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Role);
}
