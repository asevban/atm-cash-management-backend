using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.UnitTests;

public sealed class ReconciliationDifferenceCalculatorTests
{
    [Fact]
    public void Ten_missing_try_hundred_notes_equal_the_exact_approval_threshold()
    {
        var line = Line(Currency.TRY, BanknoteDenomination.OneHundred, expected: 10, counted: 0);

        var total = ReconciliationDifferenceCalculator.CalculateAbsoluteDifferenceTry(
            [line],
            new Dictionary<Currency, decimal> { [Currency.TRY] = 1m });

        Assert.Equal(1000m, total);
    }

    [Fact]
    public void Positive_and_negative_differences_do_not_cancel_each_other()
    {
        var lines = new[]
        {
            Line(Currency.TRY, BanknoteDenomination.OneHundred, expected: 15, counted: 0),
            Line(Currency.TRY, BanknoteDenomination.OneHundred, expected: 0, counted: 15)
        };

        var total = ReconciliationDifferenceCalculator.CalculateAbsoluteDifferenceTry(
            lines,
            new Dictionary<Currency, decimal> { [Currency.TRY] = 1m });

        Assert.Equal(3000m, total);
    }

    [Fact]
    public void Foreign_currency_differences_use_decimal_try_rates()
    {
        var lines = new[]
        {
            Line(Currency.USD, BanknoteDenomination.Twenty, expected: 0, counted: 1),
            Line(Currency.EUR, BanknoteDenomination.Fifty, expected: 1, counted: 0)
        };

        var total = ReconciliationDifferenceCalculator.CalculateAbsoluteDifferenceTry(
            lines,
            new Dictionary<Currency, decimal>
            {
                [Currency.USD] = 35.125m,
                [Currency.EUR] = 38.75m
            });

        Assert.Equal(2640m, total);
    }

    [Fact]
    public void Exchange_rate_requires_a_positive_supported_rate_and_try_rate_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeRate(Currency.USD, Currency.TRY, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeRate(Currency.TRY, Currency.TRY, 1.01m));
        Assert.Equal(35m, new ExchangeRate(Currency.USD, Currency.TRY, 35m).Rate);
    }

    private static CashReconciliationLine Line(
        Currency currency,
        BanknoteDenomination denomination,
        int expected,
        int counted) =>
        new(Guid.NewGuid(), Guid.NewGuid(), currency, denomination, expected, counted);
}
