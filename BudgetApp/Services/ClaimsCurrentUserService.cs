using System.Globalization;
using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Components.Authorization;

namespace BudgetApp.Services;

/// <summary>
/// Resolves the signed-in user from the cookie principal — no database round-trip; the
/// internal id was stamped as a claim at sign-in. During static/prerendering the principal
/// comes from HttpContext; inside the interactive circuit (no reliable HttpContext) it
/// comes from the circuit's AuthenticationStateProvider. Both carry the same cookie identity.
/// </summary>
public sealed class ClaimsCurrentUserService(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider) : ICurrentUserService
{
    private CurrentUser? _cached;

    public async Task<int> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
        (await GetRequiredUserAsync(cancellationToken)).Id;

    public async Task<CurrentUser> GetRequiredUserAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        var principal = httpContextAccessor.HttpContext?.User
            ?? (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return _cached = ToCurrentUser(principal);
    }

    /// <summary>Maps a cookie principal to a CurrentUser; throws when not signed in.</summary>
    public static CurrentUser ToCurrentUser(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("No signed-in user.");
        }

        var userId = principal.FindFirst(AppClaims.UserId)?.Value
            ?? throw new InvalidOperationException(
                $"Signed-in principal has no '{AppClaims.UserId}' claim — the cookie predates provisioning; sign out and back in.");
        var login = principal.FindFirst(ClaimTypes.Name)?.Value
            ?? throw new InvalidOperationException("Signed-in principal has no login (Name) claim.");
        var displayName = principal.FindFirst(GitHubAuthenticationConstants.Claims.Name)?.Value;

        return new CurrentUser(int.Parse(userId, CultureInfo.InvariantCulture), login, displayName);
    }
}
