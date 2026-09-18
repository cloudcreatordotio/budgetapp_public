namespace BudgetApp.Data.Entities;

/// <summary>A signed-in GitHub account. A row is created on first allowed sign-in.</summary>
public class User
{
    public int Id { get; set; }

    /// <summary>GitHub numeric id (the NameIdentifier claim) — the stable identity key.</summary>
    public long GitHubId { get; set; }

    public string Login { get; set; } = null!;

    /// <summary>Informational only; GitHub accounts may hide their email.</summary>
    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
