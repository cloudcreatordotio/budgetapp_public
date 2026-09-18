using BudgetApp.Services;

namespace BudgetApp.Tests;

public class TransactionPagingTests
{
    [Theory]
    [InlineData(0, 50, 1)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(1234, 50, 25)]
    public void Total_pages_rounds_up_and_never_drops_below_one(int totalCount, int pageSize, int totalPages)
    {
        var page = new TransactionPage([], 1, pageSize, totalCount, 0m, 0m);
        Assert.Equal(totalPages, page.TotalPages);
    }

    [Theory]
    [InlineData(0, 120, 50, 1)]   // below range
    [InlineData(1, 120, 50, 1)]
    [InlineData(3, 120, 50, 3)]   // last page
    [InlineData(4, 120, 50, 3)]   // past the end (e.g. after deletes)
    [InlineData(7, 0, 50, 1)]     // empty result set
    public void Requested_page_is_clamped_into_range(int requested, int totalCount, int pageSize, int expected)
    {
        Assert.Equal(expected, TransactionPage.ClampPage(requested, totalCount, pageSize));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    [InlineData(10_000, TransactionPage.MaxPageSize)]
    public void Page_size_is_bounded(int requested, int expected)
    {
        Assert.Equal(expected, TransactionPage.ClampPageSize(requested));
    }

    [Fact]
    public void Row_range_and_navigation_flags_follow_the_page()
    {
        var middle = new TransactionPage([], 2, 50, 120, 0m, 0m);
        Assert.Equal(51, middle.FirstIndex);
        Assert.Equal(100, middle.LastIndex);
        Assert.True(middle.HasPrevious);
        Assert.True(middle.HasNext);

        var last = new TransactionPage([], 3, 50, 120, 0m, 0m);
        Assert.Equal(101, last.FirstIndex);
        Assert.Equal(120, last.LastIndex);
        Assert.False(last.HasNext);

        var empty = new TransactionPage([], 1, 50, 0, 0m, 0m);
        Assert.Equal(0, empty.FirstIndex);
        Assert.Equal(0, empty.LastIndex);
        Assert.False(empty.HasPrevious);
        Assert.False(empty.HasNext);
    }
}
