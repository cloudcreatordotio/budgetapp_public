using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BudgetApp.Data;

/// <summary>
/// Lets `dotnet ef` build the model without running Program.cs — no Key Vault, no VPN,
/// no connection string. Commands like `migrations add` never open a connection.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BudgetDbContext>
{
    public BudgetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>()
            .UseSqlServer()
            .Options;
        return new BudgetDbContext(options);
    }
}
