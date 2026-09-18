namespace BudgetApp.Data.Entities;

public class Category
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Display color as "#RRGGBB".</summary>
    public string ColorHex { get; set; } = null!;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
