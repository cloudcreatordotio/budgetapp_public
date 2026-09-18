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
    bool Descending = true,
    int Page = 1,
    int PageSize = TransactionPage.DefaultPageSize);

/// <summary>
/// One page of a filtered transaction list. Totals cover every row that matches the
/// filter, not just the rows on this page, so the footer stays truthful while paging.
/// </summary>
public sealed record TransactionPage(
    IReadOnlyList<TransactionListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal IncomeTotal,
    decimal ExpenseTotal)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public static readonly int[] PageSizes = [25, 50, 100, 200];

    public int TotalPages => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
    public int FirstIndex => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastIndex => TotalCount == 0 ? 0 : Math.Min(Page * PageSize, TotalCount);

    /// <summary>Clamps a requested page size into the range the service is willing to serve.</summary>
    public static int ClampPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    /// <summary>Clamps a requested page so an out-of-range page (e.g. after deletes) still returns rows.</summary>
    public static int ClampPage(int page, int totalCount, int pageSize)
    {
        var totalPages = totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;
        return Math.Clamp(page, 1, totalPages);
    }
}

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
    /// <summary>
    /// Returns one page of the user's transactions. Only the requested page is materialised;
    /// the count and income/expense totals are aggregated in the database so the result stays
    /// bounded no matter how many transactions the user has logged.
    /// </summary>
    public async Task<TransactionPage> GetAsync(TransactionFilter filter, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = ApplyFilter(db.Transactions.AsNoTracking().Where(t => t.UserId == userId), filter);

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Income = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                Expenses = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
            })
            .SingleOrDefaultAsync(ct);

        var totalCount = totals?.Count ?? 0;
        var pageSize = TransactionPage.ClampPageSize(filter.PageSize);
        var page = TransactionPage.ClampPage(filter.Page, totalCount, pageSize);

        var items = totalCount == 0
            ? []
            : await ApplySort(query, filter)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TransactionListItem(
                    t.Id, t.Date, t.Description, t.Merchant, t.Amount, t.Type,
                    t.CategoryId, t.Category.Name, t.Category.ColorHex))
                .ToListAsync(ct);

        return new TransactionPage(items, page, pageSize, totalCount, totals?.Income ?? 0m, totals?.Expenses ?? 0m);
    }

    private static IQueryable<Transaction> ApplyFilter(IQueryable<Transaction> query, TransactionFilter filter)
    {
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

        return query;
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
