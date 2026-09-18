namespace BudgetApp.Data.Entities;

/// <summary>Stored as tinyint. No zero member, so an unset value fails validation.</summary>
public enum TransactionType : byte
{
    Income = 1,
    Expense = 2,
}
