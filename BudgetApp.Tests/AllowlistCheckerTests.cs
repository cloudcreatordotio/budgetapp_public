using BudgetApp.Services;

namespace BudgetApp.Tests;

public class AllowlistCheckerTests
{
    [Theory]
    [InlineData("alloweduser")]
    [InlineData("AllowedUser")] // GitHub logins are case-insensitive
    [InlineData("ALLOWEDUSER")]
    public void Allows_configured_login_case_insensitively(string login)
    {
        var checker = new AllowlistChecker("alloweduser");
        Assert.True(checker.IsAllowed(login));
    }

    [Fact]
    public void Allows_every_entry_of_a_comma_separated_list()
    {
        var checker = new AllowlistChecker("alloweduser,reviewer-one,Reviewer-Two");
        Assert.True(checker.IsAllowed("alloweduser"));
        Assert.True(checker.IsAllowed("reviewer-one"));
        Assert.True(checker.IsAllowed("reviewer-two"));
    }

    [Fact]
    public void Tolerates_whitespace_and_empty_entries_in_config()
    {
        var checker = new AllowlistChecker("  alloweduser , , reviewer-one  ,");
        Assert.True(checker.IsAllowed("alloweduser"));
        Assert.True(checker.IsAllowed("reviewer-one"));
    }

    [Fact]
    public void Trims_the_incoming_login_before_matching()
    {
        var checker = new AllowlistChecker("alloweduser");
        Assert.True(checker.IsAllowed(" alloweduser "));
    }

    [Fact]
    public void Rejects_a_login_that_is_not_listed()
    {
        var checker = new AllowlistChecker("alloweduser");
        Assert.False(checker.IsAllowed("someone-else"));
    }

    [Fact]
    public void Rejects_partial_and_superstring_matches()
    {
        var checker = new AllowlistChecker("alloweduser");
        Assert.False(checker.IsAllowed("allowed"));
        Assert.False(checker.IsAllowed("alloweduser2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_null_or_blank_logins(string? login)
    {
        var checker = new AllowlistChecker("alloweduser");
        Assert.False(checker.IsAllowed(login));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ,")]
    public void Empty_allowlist_rejects_everyone(string? configured)
    {
        var checker = new AllowlistChecker(configured);
        Assert.False(checker.IsAllowed("alloweduser"));
    }
}
