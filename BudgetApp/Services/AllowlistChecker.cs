namespace BudgetApp.Services;

/// <summary>
/// Decides whether a GitHub login may sign in. GitHub authenticates anyone, so the app
/// enforces its own allowlist (Authentication:GitHub:AllowedLogins, comma-separated)
/// in OnCreatingTicket — before any cookie is issued or user row created. Pure and
/// dependency-free so it can be unit-tested directly.
/// </summary>
public sealed class AllowlistChecker
{
    private readonly HashSet<string> _allowedLogins;

    /// <param name="allowedLogins">Comma-separated GitHub logins; null/empty allows nobody.</param>
    public AllowlistChecker(string? allowedLogins)
    {
        _allowedLogins = (allowedLogins ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Case-insensitive membership check; null/blank logins are never allowed.</summary>
    public bool IsAllowed(string? login) =>
        !string.IsNullOrWhiteSpace(login) && _allowedLogins.Contains(login.Trim());
}
