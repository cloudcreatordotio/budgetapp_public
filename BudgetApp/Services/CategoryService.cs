using System.ComponentModel.DataAnnotations;
using BudgetApp.Data;
using BudgetApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public sealed record CategoryItem(int Id, string Name, string ColorHex, int TransactionCount)
{
    public bool InUse => TransactionCount > 0;
}

/// <summary>Form model shared by the add and edit dialogs; validated by DataAnnotations.</summary>
public sealed class CategoryInput
{
    [Required(ErrorMessage = "A name is required.")]
    [StringLength(100, ErrorMessage = "Category names are limited to 100 characters.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "A color is required.")]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "The color must look like #4E79A7.")]
    public string? ColorHex { get; set; }
}

/// <summary>All category reads and writes, scoped to the current user.</summary>
public sealed class CategoryService(
    IDbContextFactory<BudgetDbContext> dbFactory,
    ICurrentUserService currentUser)
{
    public async Task<List<CategoryItem>> GetAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryItem(c.Id, c.Name, c.ColorHex, c.Transactions.Count))
            .ToListAsync(ct);
    }

    public async Task<ServiceResult> CreateAsync(CategoryInput input, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var name = input.Name!.Trim();
        if (await NameTakenAsync(db, userId, name, excludeId: null, ct))
        {
            return ServiceResult.Fail($"A category named \"{name}\" already exists.");
        }

        db.Categories.Add(new Category
        {
            UserId = userId,
            Name = name,
            ColorHex = NormalizeColor(input.ColorHex!),
        });
        return await SaveAsync(db, ct);
    }

    public async Task<ServiceResult> UpdateAsync(int id, CategoryInput input, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (category is null)
        {
            return ServiceResult.Fail("That category no longer exists.");
        }

        var name = input.Name!.Trim();
        if (await NameTakenAsync(db, userId, name, excludeId: id, ct))
        {
            return ServiceResult.Fail($"A category named \"{name}\" already exists.");
        }

        category.Name = name;
        category.ColorHex = NormalizeColor(input.ColorHex!);
        return await SaveAsync(db, ct);
    }

    /// <summary>Deletes a category. Blocked while transactions reference it (also enforced by the Restrict FK).</summary>
    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (category is null)
        {
            return ServiceResult.Fail("That category no longer exists.");
        }

        var inUseCount = await db.Transactions.CountAsync(t => t.CategoryId == id, ct);
        if (inUseCount > 0)
        {
            return ServiceResult.Fail(
                $"\"{category.Name}\" is used by {inUseCount} transaction(s). Reassign or delete them first.");
        }

        db.Categories.Remove(category);
        return await SaveAsync(db, ct);
    }

    private static Task<bool> NameTakenAsync(BudgetDbContext db, int userId, string name, int? excludeId, CancellationToken ct) =>
        // SQL Server's case-insensitive collation makes == a case-insensitive comparison,
        // matching the unique index on (UserId, Name).
        db.Categories.AnyAsync(c => c.UserId == userId && c.Name == name && c.Id != excludeId, ct);

    private static string NormalizeColor(string colorHex) => colorHex.Trim().ToUpperInvariant();

    private static async Task<ServiceResult> SaveAsync(BudgetDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return ServiceResult.Success;
        }
        catch (DbUpdateException)
        {
            // Racing writes can still hit the unique index or the Restrict FK.
            return ServiceResult.Fail("The change conflicted with another update. Reload and try again.");
        }
    }
}
