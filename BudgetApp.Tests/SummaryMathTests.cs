using BudgetApp.Data.Entities;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class SummaryMathTests
{
    [Theory]
    [InlineData(4200, 1500, 2700)]
    [InlineData(0, 250.50, -250.50)]
    [InlineData(0, 0, 0)]
    public void Monthly_balance_is_income_minus_expenses(decimal income, decimal expenses, decimal balance)
    {
        Assert.Equal(balance, new MonthlySummary(income, expenses).Balance);
        Assert.Equal(balance, new MonthTotals(2026, 9, income, expenses).Balance);
    }

    [Fact]
    public void Signed_money_carries_the_type_sign()
    {
        Assert.Equal("+$25.00", Fmt.SignedMoney(25m, TransactionType.Income));
        Assert.Equal("-$25.00", Fmt.SignedMoney(25m, TransactionType.Expense));
    }

    [Fact]
    public void Money_is_en_us_currency_regardless_of_host_culture()
    {
        Assert.Equal("$1,234.56", Fmt.Money(1234.56m));
    }
}
