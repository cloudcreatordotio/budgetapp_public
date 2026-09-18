namespace BudgetApp.Data.Entities;

public class Transaction
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string Merchant { get; set; } = null!;

    /// <summary>Always positive; <see cref="Type"/> carries the sign.</summary>
    public decimal Amount { get; set; }

    public TransactionType Type { get; set; }

    public DateOnly Date { get; set; }
}
