using System.Globalization;
using BudgetApp.Data.Entities;

namespace BudgetApp.Services;

/// <summary>
/// Display formatting for money and dates. Pinned to en-US so amounts render as "$1,234.56"
/// regardless of host culture (the app is dollar-denominated; see README assumptions).
/// </summary>
public static class Fmt
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal amount) => amount.ToString("C2", Culture);

    /// <summary>"+$25.00" for income, "-$25.00" for expenses.</summary>
    public static string SignedMoney(decimal amount, TransactionType type) =>
        (type == TransactionType.Income ? "+" : "-") + Money(amount);

    public static string AmountClass(TransactionType type) =>
        type == TransactionType.Income ? "amount-pos" : "amount-neg";

    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", Culture);

    public static string MonthYear(DateOnly date) => date.ToString("MMMM yyyy", Culture);
}
