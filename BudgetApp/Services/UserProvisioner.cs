using BudgetApp.Data;
using BudgetApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>
/// Creates or refreshes the User row for an allowed GitHub sign-in. Runs inside
/// OnCreatingTicket, after the allowlist check. First sign-in also seeds the
/// default category set.
/// </summary>
public sealed class UserProvisioner(
    IDbContextFactory<BudgetDbContext> dbFactory,
    ILogger<UserProvisioner> logger)
{
    public async Task<User> EnsureUserAsync(
        long gitHubId, string login, string? email, string? displayName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users.SingleOrDefaultAsync(u => u.GitHubId == gitHubId, ct);
        if (user is null)
        {
            user = new User
            {
                GitHubId = gitHubId,
                Login = login,
                Email = email,
                DisplayName = displayName,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Users.Add(user);
            db.Categories.AddRange(SeedData.CreateDefaultCategoriesFor(user));
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Provisioned new user '{Login}' (GitHub id {GitHubId}) with {Count} default categories.",
                login, gitHubId, SeedData.DefaultCategories.Count);
        }
        else if (user.Login != login || user.Email != email || user.DisplayName != displayName)
        {
            // GitHub profile fields can change between sign-ins; the numeric id is the identity.
            user.Login = login;
            user.Email = email;
            user.DisplayName = displayName;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Refreshed profile fields for user '{Login}' (GitHub id {GitHubId}).", login, gitHubId);
        }

        return user;
    }
}
