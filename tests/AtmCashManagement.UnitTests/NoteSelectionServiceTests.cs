using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.UnitTests;

public sealed class NoteSelectionServiceTests
{
    private readonly NoteSelectionService _service = new();

    [Fact]
    public void B3_acceptance_01_returns_the_exact_minimum_note_combination()
    {
        var result = _service.Select(Currency.TRY, 160,
        [
            Stock(1, BanknoteDenomination.OneHundred, 0),
            Stock(2, BanknoteDenomination.Fifty, 3),
            Stock(3, BanknoteDenomination.Twenty, 10)
        ]);

        Assert.Equal(NoteSelectionOutcome.Exact, result.Outcome);
        AssertOption(result.Exact, 160, 5, (BanknoteDenomination.Fifty, 2), (BanknoteDenomination.Twenty, 3));
        Assert.Null(result.Lower);
        Assert.Null(result.Upper);
    }

    [Fact]
    public void B3_acceptance_02_returns_the_closest_lower_and_upper_options()
    {
        var result = _service.Select(Currency.TRY, 80,
        [
            Stock(1, BanknoteDenomination.Fifty, 1),
            Stock(2, BanknoteDenomination.Twenty, 2)
        ]);

        Assert.Equal(NoteSelectionOutcome.Alternatives, result.Outcome);
        Assert.Null(result.Exact);
        AssertOption(result.Lower, 70, 2, (BanknoteDenomination.Fifty, 1), (BanknoteDenomination.Twenty, 1));
        AssertOption(result.Upper, 90, 3, (BanknoteDenomination.Fifty, 1), (BanknoteDenomination.Twenty, 2));
    }

    [Fact]
    public void B3_acceptance_03_returns_the_closest_lower_and_upper_options()
    {
        var result = _service.Select(Currency.TRY, 130,
        [
            Stock(1, BanknoteDenomination.OneHundred, 5),
            Stock(2, BanknoteDenomination.Twenty, 10)
        ]);

        Assert.Equal(NoteSelectionOutcome.Alternatives, result.Outcome);
        AssertOption(result.Lower, 120, 2, (BanknoteDenomination.OneHundred, 1), (BanknoteDenomination.Twenty, 1));
        AssertOption(result.Upper, 140, 3, (BanknoteDenomination.OneHundred, 1), (BanknoteDenomination.Twenty, 2));
    }

    [Fact]
    public void B3_acceptance_04_returns_lower_and_no_upper_when_stock_is_insufficient()
    {
        var result = _service.Select(Currency.USD, 250,
        [
            Stock(1, BanknoteDenomination.OneHundred, 2, currency: Currency.USD)
        ]);

        Assert.Equal(NoteSelectionOutcome.Alternatives, result.Outcome);
        AssertOption(result.Lower, 200, 2, (BanknoteDenomination.OneHundred, 2));
        Assert.Null(result.Upper);
    }

    [Fact]
    public void B3_acceptance_05_returns_the_exact_mixed_denomination_combination()
    {
        var result = _service.Select(Currency.TRY, 600,
        [
            Stock(1, BanknoteDenomination.FiveHundred, 1),
            Stock(2, BanknoteDenomination.OneHundred, 0),
            Stock(3, BanknoteDenomination.Twenty, 5)
        ]);

        Assert.Equal(NoteSelectionOutcome.Exact, result.Outcome);
        AssertOption(result.Exact, 600, 6, (BanknoteDenomination.FiveHundred, 1), (BanknoteDenomination.Twenty, 5));
    }

    [Fact]
    public void B3_acceptance_06_uses_the_minimum_three_notes()
    {
        var result = _service.Select(Currency.TRY, 200,
        [
            Stock(1, BanknoteDenomination.OneHundred, 1),
            Stock(2, BanknoteDenomination.Fifty, 4),
            Stock(3, BanknoteDenomination.Twenty, 5)
        ]);

        Assert.Equal(NoteSelectionOutcome.Exact, result.Outcome);
        AssertOption(result.Exact, 200, 3, (BanknoteDenomination.OneHundred, 1), (BanknoteDenomination.Fifty, 2));
    }

    [Fact]
    public void B3_acceptance_07_returns_the_exact_minimum_note_combination()
    {
        var result = _service.Select(Currency.TRY, 1000,
        [
            Stock(1, BanknoteDenomination.FiveHundred, 1),
            Stock(2, BanknoteDenomination.OneHundred, 3),
            Stock(3, BanknoteDenomination.Twenty, 20)
        ]);

        Assert.Equal(NoteSelectionOutcome.Exact, result.Outcome);
        AssertOption(result.Exact, 1000, 14,
            (BanknoteDenomination.FiveHundred, 1),
            (BanknoteDenomination.OneHundred, 3),
            (BanknoteDenomination.Twenty, 10));
    }

    [Fact]
    public void B3_acceptance_08_returns_the_closest_lower_and_upper_options()
    {
        var result = _service.Select(Currency.TRY, 110,
        [
            Stock(1, BanknoteDenomination.OneHundred, 3),
            Stock(2, BanknoteDenomination.Fifty, 1),
            Stock(3, BanknoteDenomination.Twenty, 1)
        ]);

        Assert.Equal(NoteSelectionOutcome.Alternatives, result.Outcome);
        AssertOption(result.Lower, 100, 1, (BanknoteDenomination.OneHundred, 1));
        AssertOption(result.Upper, 120, 2, (BanknoteDenomination.OneHundred, 1), (BanknoteDenomination.Twenty, 1));
    }

    [Fact]
    public void Empty_atm_returns_a_meaningful_no_cash_result()
    {
        var result = _service.Select(Currency.TRY, 20, []);

        Assert.Equal(NoteSelectionOutcome.NoAvailableCash, result.Outcome);
        Assert.Null(result.Exact);
        Assert.Null(result.Lower);
        Assert.Null(result.Upper);
    }

    [Fact]
    public void Stock_in_another_currency_is_not_available_for_the_request()
    {
        var result = _service.Select(Currency.TRY, 100,
        [Stock(1, BanknoteDenomination.OneHundred, 2, currency: Currency.USD)]);

        Assert.Equal(NoteSelectionOutcome.NoAvailableCash, result.Outcome);
    }

    [Fact]
    public void Deposit_only_cassette_cash_is_never_selected()
    {
        var result = _service.Select(Currency.TRY, 20,
        [new CassetteStockInput(1, CassetteType.DepositOnly, 100, null, null, 20, 0)]);

        Assert.Equal(NoteSelectionOutcome.NoAvailableCash, result.Outcome);
    }

    [Fact]
    public void Reserved_notes_are_excluded_from_available_stock()
    {
        var result = _service.Select(Currency.TRY, 100,
        [Stock(1, BanknoteDenomination.Fifty, 2, reserved: 1)]);

        AssertOption(result.Lower, 50, 1, (BanknoteDenomination.Fifty, 1));
        Assert.Null(result.Exact);
        Assert.Null(result.Upper);
    }

    [Fact]
    public void Same_denomination_can_be_selected_from_multiple_cassettes()
    {
        var result = _service.Select(Currency.TRY, 100,
        [
            Stock(1, BanknoteDenomination.Fifty, 1),
            Stock(2, BanknoteDenomination.Fifty, 1)
        ]);

        AssertOption(result.Exact, 100, 2, (BanknoteDenomination.Fifty, 2));
        Assert.Equal(new[] { (1, 1), (2, 1) }, result.Exact!.Allocations
            .Select(allocation => (allocation.CassetteNumber, allocation.Count)).ToArray());
    }

    [Fact]
    public void Higher_physical_fill_ratio_cassette_is_used_first_for_the_same_denomination()
    {
        var result = _service.Select(Currency.TRY, 100,
        [
            Stock(1, BanknoteDenomination.OneHundred, 2, capacity: 10),
            Stock(2, BanknoteDenomination.OneHundred, 8, capacity: 10)
        ]);

        AssertOption(result.Exact, 100, 1, (BanknoteDenomination.OneHundred, 1));
        Assert.Equal(2, Assert.Single(result.Exact!.Allocations).CassetteNumber);
    }

    [Fact]
    public void Equal_fill_ratio_uses_lower_cassette_slot_first()
    {
        var result = _service.Select(Currency.TRY, 100,
        [
            Stock(2, BanknoteDenomination.OneHundred, 4, capacity: 10),
            Stock(1, BanknoteDenomination.OneHundred, 2, capacity: 5)
        ]);

        Assert.Equal(1, Assert.Single(result.Exact!.Allocations).CassetteNumber);
    }

    [Fact]
    public void Available_notes_stay_within_capacity_and_exclude_reservations()
    {
        var result = _service.Select(Currency.TRY, 60,
        [Stock(1, BanknoteDenomination.Twenty, 10, reserved: 7, capacity: 10)]);

        AssertOption(result.Exact, 60, 3, (BanknoteDenomination.Twenty, 3));
        Assert.All(result.Exact!.Allocations, allocation => Assert.InRange(allocation.Count, 1, 3));
    }

    [Fact]
    public void Physical_stock_cannot_exceed_cassette_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Select(Currency.TRY, 20,
            [Stock(1, BanknoteDenomination.Twenty, 11, capacity: 10)]));
    }

    [Fact]
    public void Insufficient_total_stock_returns_the_largest_payable_lower_amount()
    {
        var result = _service.Select(Currency.TRY, 100,
        [Stock(1, BanknoteDenomination.Twenty, 2)]);

        AssertOption(result.Lower, 40, 2, (BanknoteDenomination.Twenty, 2));
        Assert.Null(result.Upper);
    }

    [Fact]
    public void Upper_alternative_is_hidden_when_it_exceeds_the_remaining_daily_limit()
    {
        var result = _service.Select(Currency.TRY, 80,
        [
            Stock(1, BanknoteDenomination.Fifty, 1),
            Stock(2, BanknoteDenomination.Twenty, 2)
        ], remainingDailyLimit: 80);

        AssertOption(result.Lower, 70, 2, (BanknoteDenomination.Fifty, 1), (BanknoteDenomination.Twenty, 1));
        Assert.Null(result.Upper);
    }

    [Fact]
    public void Exact_payout_over_the_remaining_limit_is_replaced_by_a_permitted_lower_option()
    {
        var result = _service.Select(Currency.TRY, 100,
        [
            Stock(1, BanknoteDenomination.OneHundred, 1),
            Stock(2, BanknoteDenomination.Twenty, 4)
        ], remainingDailyLimit: 80);

        Assert.Equal(NoteSelectionOutcome.Alternatives, result.Outcome);
        Assert.Null(result.Exact);
        AssertOption(result.Lower, 80, 4, (BanknoteDenomination.Twenty, 4));
        Assert.Null(result.Upper);
    }

    [Fact]
    public void No_option_is_returned_when_every_payout_exceeds_the_remaining_daily_limit()
    {
        var result = _service.Select(Currency.TRY, 20,
        [Stock(1, BanknoteDenomination.Twenty, 1)], remainingDailyLimit: 19);

        Assert.Equal(NoteSelectionOutcome.NoPermittedPayout, result.Outcome);
        Assert.Null(result.Exact);
        Assert.Null(result.Lower);
        Assert.Null(result.Upper);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_requested_amount_is_rejected(int requestedAmount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Select(Currency.TRY, requestedAmount, []));
    }

    [Fact]
    public void Reserved_count_cannot_exceed_physical_stock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Select(Currency.TRY, 20,
            [Stock(1, BanknoteDenomination.Twenty, 1, reserved: 2)]));
    }

    [Theory]
    [InlineData(Currency.TRY)]
    [InlineData(Currency.USD)]
    [InlineData(Currency.EUR)]
    public void All_supported_currencies_can_be_selected(Currency currency)
    {
        var result = _service.Select(currency, 100,
        [Stock(1, BanknoteDenomination.OneHundred, 1, currency: currency)]);

        AssertOption(result.Exact, 100, 1, (BanknoteDenomination.OneHundred, 1));
    }

    [Fact]
    public void Identical_input_produces_identical_deterministic_allocations()
    {
        var stock = new[]
        {
            Stock(1, BanknoteDenomination.OneHundred, 2),
            Stock(2, BanknoteDenomination.Fifty, 4),
            Stock(3, BanknoteDenomination.Twenty, 5)
        };

        var first = _service.Select(Currency.TRY, 200, stock);
        var second = _service.Select(Currency.TRY, 200, stock);

        Assert.Equal(OptionSignature(first.Exact), OptionSignature(second.Exact));
    }

    [Fact]
    public void Notes_remain_unmodified_after_selection()
    {
        var stock = Stock(1, BanknoteDenomination.Fifty, 2, reserved: 1);

        _service.Select(Currency.TRY, 50, [stock]);

        Assert.Equal(2, stock.PhysicalCount);
        Assert.Equal(1, stock.ReservedCount);
    }

    private static CassetteStockInput Stock(
        int number,
        BanknoteDenomination denomination,
        int physical,
        int reserved = 0,
        int capacity = 100,
        Currency currency = Currency.TRY,
        CassetteType type = CassetteType.Recycling) =>
        new(number, type, capacity, type == CassetteType.Recycling ? currency : null,
            type == CassetteType.Recycling ? denomination : null, physical, reserved);

    private static void AssertOption(
        NoteSelectionOption? option,
        int expectedAmount,
        int expectedNoteCount,
        params (BanknoteDenomination Denomination, int Count)[] expectedCounts)
    {
        Assert.NotNull(option);
        Assert.Equal(expectedAmount, option.Amount);
        Assert.Equal(expectedNoteCount, option.NoteCount);
        Assert.Equal(expectedNoteCount, option.Allocations.Sum(allocation => allocation.Count));
        Assert.Equal(expectedAmount, option.Allocations.Sum(allocation => allocation.Count * (int)allocation.Denomination));

        var actualCounts = option.Allocations
            .GroupBy(allocation => allocation.Denomination)
            .Select(group => (Denomination: group.Key, Count: group.Sum(allocation => allocation.Count)))
            .OrderBy(item => item.Denomination)
            .ToArray();
        Assert.Equal(expectedCounts.OrderBy(item => item.Denomination), actualCounts);
    }

    private static string OptionSignature(NoteSelectionOption? option) => option is null
        ? "null"
        : $"{option.Amount}:{option.NoteCount}:" + string.Join(',', option.Allocations
            .Select(allocation => $"{allocation.CassetteNumber}-{allocation.Denomination}-{allocation.Count}"));
}
