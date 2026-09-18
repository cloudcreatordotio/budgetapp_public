using System.ComponentModel.DataAnnotations;
using BudgetApp.Data;
using BudgetApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public enum TransactionSort
{
    Date,
    Description,
    Merchant,
    Category,
    Type,
    Amount,
}

public sealed record TransactionFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    int? CategoryId = null,
    TransactionType? Type = null,
    TransactionSort Sort = TransactionSort.Date,
    bool Descending = true);

public sealed record TransactionListItem(
    int Id,
    DateOnly Date,
    string Description,
    string Merchant,
    decimal Amount,
    TransactionType Type,
    int CategoryId,
    string CategoryName,
    string CategoryColorHex);

/// <summary>Form model shared by the create and edit dialogs; validated by DataAnnotations.</summary>
public sealed class TransactionInput
{
    [Required(ErrorMessage = "A description is required.")]
    [StringLength(200, ErrorMessage = "Descriptions are limited to 200 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "A merchant is required.")]
    [StringLength(100, ErrorMessage = "Merchant names are limited to 100 characters.")]
    public string? Merchant { get; set; }

    [Required(ErrorMessage = "An amount is required.")]
    [Range(0.01, 999_999_999_999.99, ErrorMessage = "The amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Choose income or expense.")]
    public TransactionType? Type { get; set; }

    [Required(ErrorMessage = "A date is required.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Choose a category.")]
    public int? CategoryId { get; set; }
}

/// <summary>All transaction reads and writes, scoped to the current user.</summary>
public sealed class TransactionService(
    IDbContextFactory<BudgetDbContext> dbFactory,
    ICurrentUserService currentUser)
{
    public async Task<List<TransactionListItem>> GetAsync(TransactionFilter filter, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.Transactions.AsNoTracking().Where(t => t.UserId == userId);
        if (filter.From is { } from)
        {
            query = query.Where(t => t.Date >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(t => t.Date <= to);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(t => t.CategoryId == categoryId);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(t => t.Type == type);
        }

        return await ApplySort(query, filter)
            .Select(t => new TransactionListItem(
                t.Id, t.Date, t.Description, t.Merchant, t.Amount, t.Type,
                t.CategoryId, t.Category.Name, t.Category.ColorHex))
            .ToListAsync(ct);
    }

    public async Task<ServiceResult> CreateAsync(TransactionInput input, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (!await OwnsCategoryAsync(db, userId, input.CategoryId!.Value, ct))
        {
            return ServiceResult.Fail("That category no longer exists.");
        }

        db.Transactions.Add(new Transaction
        {
            UserId = userId,
            CategoryId = input.CategoryId!.Value,
            Description = input.Description!.Trim(),
            Merchant = input.Merchant!.Trim(),
            Amount = input.Amount!.Value,
            Type = input.Type!.Value,
            Date = input.Date!.Value,
        });
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> UpdateAsync(int id, TransactionInput input, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var transaction = await db.Transactions
            .SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (transaction is null)
        {
            return ServiceResult.Fail("That transaction no longer exists.");
        }

        if (!await OwnsCategoryAsync(db, userId, input.CategoryId!.Value, ct))
        {
            return ServiceResult.Fail("That category no longer exists.");
        }

        transaction.CategoryId = input.CategoryId!.Value;
        transaction.Description = input.Description!.Trim();
        transaction.Merchant = input.Merchant!.Trim();
        transaction.Amount = input.Amount!.Value;
        transaction.Type = input.Type!.Value;
        transaction.Date = input.Date!.Value;
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var deleted = await db.Transactions
            .Where(t => t.Id == id && t.UserId == userId)
            .ExecuteDeleteAsync(ct);
        return deleted > 0 ? ServiceResult.Success : ServiceResult.Fail("That transaction no longer exists.");
    }

    private static Task<bool> OwnsCategoryAsync(BudgetDbContext db, int userId, int categoryId, CancellationToken ct) =>
        db.Categories.AnyAsync(c => c.Id == categoryId && c.UserId == userId, ct);

    private static IOrderedQueryable<Transaction> ApplySort(IQueryable<Transaction> query, TransactionFilter filter)
    {
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (TransactionSort.Date, false) => query.OrderBy(t => t.Date),
            (TransactionSort.Date, true) => query.OrderByDescending(t => t.Date),
            (TransactionSort.Description, false) => query.OrderBy(t => t.Description),
            (TransactionSort.Description, true) => query.OrderByDescending(t => t.Description),
            (TransactionSort.Merchant, false) => query.OrderBy(t => t.Merchant),
            (TransactionSort.Merchant, true) => query.OrderByDescending(t => t.Merchant),
            (TransactionSort.Category, false) => query.OrderBy(t => t.Category.Name),
            (TransactionSort.Category, true) => query.OrderByDescending(t => t.Category.Name),
            (TransactionSort.Type, false) => query.OrderBy(t => t.Type),
            (TransactionSort.Type, true) => query.OrderByDescending(t => t.Type),
            (TransactionSort.Amount, false) => query.OrderBy(t => t.Amount),
            _ => query.OrderByDescending(t => t.Amount),
        };

        // Stable secondary ordering so equal keys don't shuffle between reloads.
        return sorted.ThenByDescending(t => t.Date).ThenByDescending(t => t.Id);
    }
}
