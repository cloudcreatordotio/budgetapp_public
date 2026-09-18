using BudgetApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

public static class SeedData
{
    /// <summary>Well-known identity of the offline dev user; negative so it can never collide with a real GitHub id.</summary>
    public const long DevUserGitHubId = -1;

    public const string DevUserLogin = "dev";

    /// <summary>Default category set every new user starts with (also seeded on first sign-in from Session 3 on).</summary>
    public static readonly IReadOnlyList<(string Name, string ColorHex)> DefaultCategories =
    [
        ("Groceries", "#4E79A7"),
        ("Rent", "#F28E2B"),
        ("Utilities", "#E15759"),
        ("Transport", "#76B7B2"),
        ("Dining Out", "#59A14F"),
        ("Entertainment", "#EDC948"),
        ("Shopping", "#B07AA1"),
        ("Health", "#FF9DA7"),
        ("Bills", "#9C755F"),
        ("Salary", "#86BCB6"),
        ("Other", "#BAB0AC"),
    ];

    public static List<Category> CreateDefaultCategoriesFor(User user) =>
        DefaultCategories.Select(c => new Category { User = user, Name = c.Name, ColorHex = c.ColorHex }).ToList();

    /// <summary>
    /// Seeds the dev user, their default categories, and three months of sample
    /// transactions. Only runs when Database:SeedSampleData=true (offline mode) and is
    /// idempotent — it does nothing once the dev user exists.
    /// </summary>
    public static async Task SeedSampleDataAsync(BudgetDbContext db, ILogger logger)
    {
        if (await db.Users.AnyAsync(u => u.GitHubId == DevUserGitHubId))
        {
            logger.LogInformation("Sample data already present (dev user exists) — seeding skipped.");
            return;
        }

        var devUser = new User
        {
            GitHubId = DevUserGitHubId,
            Login = DevUserLogin,
            DisplayName = "Dev User",
            CreatedUtc = DateTime.UtcNow,
        };
        var categories = CreateDefaultCategoriesFor(devUser);
        var transactions = CreateSampleTransactions(devUser, categories, DateOnly.FromDateTime(DateTime.Today));

        db.Users.Add(devUser);
        db.Categories.AddRange(categories);
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded dev user '{Login}' with {Categories} categories and {Transactions} sample transactions.",
            devUser.Login, categories.Count, transactions.Count);
    }

    private static List<Transaction> CreateSampleTransactions(User user, IReadOnlyList<Category> categories, DateOnly today)
    {
        var byName = categories.ToDictionary(c => c.Name);
        var rng = new Random(20260901); // fixed seed — deterministic sample data
        var transactions = new List<Transaction>();

        // Current month plus the two before it. Day numbers stay ≤ 26 so they exist in
        // every month; dates after today are skipped.
        for (var offset = 2; offset >= 0; offset--)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-offset);

            void Add(string category, string description, string merchant, decimal amount, int day,
                TransactionType type = TransactionType.Expense)
            {
                var date = monthStart.AddDays(day - 1);
                if (date > today)
                {
                    return;
                }

                transactions.Add(new Transaction
                {
                    User = user,
                    Category = byName[category],
                    Description = description,
                    Merchant = merchant,
                    Amount = amount,
                    Type = type,
                    Date = date,
                });
            }

            decimal Vary(decimal baseline) => Math.Round(baseline * (0.85m + (decimal)rng.NextDouble() * 0.30m), 2);

            Add("Salary", "Monthly salary", "Acme Corp", 4200.00m, 1, TransactionType.Income);
            Add("Rent", "Apartment rent", "Maple Court Apartments", 1450.00m, 1);
            Add("Utilities", "Electricity", "Metro Power & Light", Vary(96m), 6);
            Add("Utilities", "Water and sewer", "City Utilities", Vary(38m), 12);
            Add("Bills", "Internet", "FiberLink", 69.99m, 3);
            Add("Bills", "Mobile plan", "Cellular One", 45.00m, 15);
            Add("Groceries", "Weekly groceries", "Whole Foods", Vary(122m), 2);
            Add("Groceries", "Weekly groceries", "Trader Joe's", Vary(86m), 9);
            Add("Groceries", "Weekly groceries", "Whole Foods", Vary(108m), 16);
            Add("Groceries", "Bulk shop", "Costco", Vary(164m), 23);
            Add("Transport", "Transit card top-up", "Metro Transit", 33.00m, 4);
            Add("Transport", "Fuel", "Shell", Vary(52m), 14);
            Add("Transport", "Ride share", "Uber", Vary(24m), 21);
            Add("Dining Out", "Lunch", "Chipotle", Vary(16m), 5);
            Add("Dining Out", "Dinner out", "Trattoria Roma", Vary(58m), 11);
            Add("Dining Out", "Coffee", "Blue Bottle", Vary(14m), 18);
            Add("Dining Out", "Takeout", "Thai Palace", Vary(34m), 26);
            Add("Entertainment", "Streaming subscription", "Netflix", 15.49m, 8);
            Add("Entertainment", "Movie night", "AMC Theatres", Vary(32m), 20);
            Add("Shopping", "Household items", "Amazon", Vary(64m), 13);
            Add("Shopping", "Clothes", "Uniqlo", Vary(78m), 24);
            Add("Health", "Pharmacy", "CVS", Vary(28m), 17);
            Add("Other", "Gym membership", "Planet Fitness", 24.99m, 10);
            if (offset == 1)
            {
                Add("Salary", "Freelance project", "Side Project LLC", 650.00m, 22, TransactionType.Income);
            }
        }

        return transactions;
    }
}
