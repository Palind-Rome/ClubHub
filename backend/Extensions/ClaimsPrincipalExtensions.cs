using System.Security.Claims;

namespace ClubHub.Extensions;

/// <summary>
/// Extension methods for <see cref="ClaimsPrincipal"/> to access JWT claims.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the authenticated user ID from the JWT <c>NameIdentifier</c> claim.
    /// </summary>
    /// <returns>The user ID, or <c>null</c> if the claim is missing or invalid.</returns>
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }
}
