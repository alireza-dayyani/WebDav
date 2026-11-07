using System.Security.Claims;

namespace Inkstone.Web.Services;

public static class Ownership
{
    public static string Owner(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated account identity is missing.");
}
