namespace BudgetApp.Services;

/// <summary>Identity of the signed-in user, for display and scoping.</summary>
public sealed record CurrentUser(int Id, string Login, string? DisplayName)
{
    public string Name => DisplayName ?? Login;
}

/// <summary>
/// Resolves the signed-in user. Every query goes through services scoped by
/// this user's id — no page or service may touch another user's rows.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The current user's id; throws when no user can be resolved.</summary>
    Task<int> GetRequiredUserIdAsync(CancellationToken cancellationToken = default);

    /// <summary>The current user with display fields; throws when no user can be resolved.</summary>
    Task<CurrentUser> GetRequiredUserAsync(CancellationToken cancellationToken = default);
}
