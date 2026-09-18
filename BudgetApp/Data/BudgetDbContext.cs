using BudgetApp.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

public class BudgetDbContext(DbContextOptions<BudgetDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    /// <summary>ASP.NET Core data-protection key ring — persisted so container restarts don't sign users out.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Login).HasMaxLength(100).IsRequired();
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.DisplayName).HasMaxLength(200);
            user.HasIndex(u => u.GitHubId).IsUnique();
            user.HasIndex(u => u.Login).IsUnique();
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.Property(c => c.Name).HasMaxLength(100).IsRequired();
            category.Property(c => c.ColorHex).HasMaxLength(7).IsFixedLength().IsUnicode(false).IsRequired();
            // SQL Server's default collation is case-insensitive, which makes this the
            // case-insensitive (UserId, Name) uniqueness the domain rules require.
            category.HasIndex(c => new { c.UserId, c.Name }).IsUnique();
            category.HasOne(c => c.User)
                .WithMany(u => u.Categories)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.Property(t => t.Description).HasMaxLength(200).IsRequired();
            transaction.Property(t => t.Merchant).HasMaxLength(100).IsRequired();
            transaction.Property(t => t.Amount).HasPrecision(18, 2);
            transaction.Property(t => t.Date).HasColumnType("date");
            transaction.HasIndex(t => new { t.UserId, t.Date });
            transaction.ToTable(t => t.HasCheckConstraint("CK_Transactions_Amount_Positive", "[Amount] > 0"));
            transaction.HasOne(t => t.User)
                .WithMany(u => u.Transactions)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict: a category cannot be deleted while transactions still reference it.
            transaction.HasOne(t => t.Category)
                .WithMany(c => c.Transactions)
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
