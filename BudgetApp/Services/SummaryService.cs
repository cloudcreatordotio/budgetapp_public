using BudgetApp.Data;
using BudgetApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public sealed record MonthlySummary(decimal Income, decimal Expenses)
{
    /// <summary>What's left of the month's income — the "saved" side of saved vs. spent.</summary>
    public decimal Balance => Income - Expenses;
}

public sealed record CategorySpend(string Name, string ColorHex, decimal Total);

/// <summary>One month's income/expense totals — a bar pair on the Reports chart.</summary>
public sealed record MonthTotals(int Year, int Month, decimal Income, decimal Expenses)
{
    public decimal Balance => Income - Expenses;
    public DateOnly MonthStart => new(Year, Month, 1);
}

/// <summary>Dashboard aggregates, scoped to the current user.</summary>
public sealed class SummaryService(
    IDbContextFactory<BudgetDbContext> dbFactory,
    ICurrentUserService currentUser)
{
    public async Task<MonthlySummary> GetMonthlySummaryAsync(int year, int month, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var totals = await MonthQuery(db, userId, year, month)
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        return new MonthlySummary(
            Income: totals.SingleOrDefault(t => t.Type == TransactionType.Income)?.Total ?? 0m,
            Expenses: totals.SingleOrDefault(t => t.Type == TransactionType.Expense)?.Total ?? 0m);
    }

    /// <summary>Expense totals per category for one month, largest first — the donut's slices.</summary>
    public async Task<List<CategorySpend>> GetMonthlyCategorySpendAsync(int year, int month, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Anonymous projection: EF can't translate a constructor call over a GroupBy.
        var rows = await MonthQuery(db, userId, year, month)
            .Where(t => t.Type == TransactionType.Expense)
            .GroupBy(t => new { t.Category.Name, t.Category.ColorHex })
            .Select(g => new { g.Key.Name, g.Key.ColorHex, Total = g.Sum(t => t.Amount) })
            .OrderByDescending(r => r.Total)
            .ToListAsync(ct);
        return rows.Select(r => new CategorySpend(r.Name, r.ColorHex, r.Total)).ToList();
    }

    public async Task<List<TransactionListItem>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Take(count)
            .Select(t => new TransactionListItem(
                t.Id, t.Date, t.Description, t.Merchant, t.Amount, t.Type,
                t.CategoryId, t.Category.Name, t.Category.ColorHex))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Income/expense totals for the last <paramref name="months"/> calendar months up to and
    /// including the current one, oldest first. Months with no transactions appear as zeros.
    /// </summary>
    public async Task<List<MonthTotals>> GetMonthOverMonthAsync(int months, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        // Anonymous projection: EF can't translate a constructor call over a GroupBy.
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.Date >= start)
            .GroupBy(t => new { t.Date.Year, t.Date.Month, t.Type })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Type, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        return Enumerable.Range(0, months)
            .Select(i =>
            {
                var m = start.AddMonths(i);
                return new MonthTotals(m.Year, m.Month,
                    Income: rows.SingleOrDefault(r =>
                        r.Year == m.Year && r.Month == m.Month && r.Type == TransactionType.Income)?.Total ?? 0m,
                    Expenses: rows.SingleOrDefault(r =>
                        r.Year == m.Year && r.Month == m.Month && r.Type == TransactionType.Expense)?.Total ?? 0m);
            })
            .ToList();
    }

    /// <summary>First month with any transaction, or null — bounds the Reports month picker.</summary>
    public async Task<DateOnly?> GetFirstTransactionMonthAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var first = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .MinAsync(t => (DateOnly?)t.Date, ct);
        return first is { } d ? new DateOnly(d.Year, d.Month, 1) : null;
    }

    private static IQueryable<Transaction> MonthQuery(BudgetDbContext db, int userId, int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var toExclusive = from.AddMonths(1);
        return db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.Date >= from && t.Date < toExclusive);
    }
}
