using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.UnitTests;

public sealed class DepositAllocationPlannerTests
{
    private readonly DepositAllocationPlanner _planner = new();

    [Fact]
    public void Matching_notes_are_sent_to_the_configured_recycling_cassette()
    {
        var plan = _planner.Plan(Cassettes(), [new(Currency.TRY, BanknoteDenomination.Fifty, 12)]);

        Assert.Equal(new DepositPlacementResponse(3, Currency.TRY, BanknoteDenomination.Fifty, 12), Assert.Single(plan.Placements));
        Assert.Empty(plan.CapacityReturns);
    }

    [Fact]
    public void Unmatched_notes_are_sent_to_the_deposit_only_cassette()
    {
        var plan = _planner.Plan(Cassettes(), [new(Currency.EUR, BanknoteDenomination.FiveHundred, 4)]);

        Assert.Equal(new DepositPlacementResponse(1, Currency.EUR, BanknoteDenomination.FiveHundred, 4), Assert.Single(plan.Placements));
        Assert.Empty(plan.CapacityReturns);
    }

    [Fact]
    public void Duplicate_recycling_slots_are_filled_by_slot_number()
    {
        var cassettes = Cassettes(duplicateTryHundred: true, firstDuplicateCapacity: 2, secondDuplicateCapacity: 5);

        var plan = _planner.Plan(cassettes, [new(Currency.TRY, BanknoteDenomination.OneHundred, 6)]);

        Assert.Equal(new[]
        {
            new DepositPlacementResponse(2, Currency.TRY, BanknoteDenomination.OneHundred, 2),
            new DepositPlacementResponse(5, Currency.TRY, BanknoteDenomination.OneHundred, 4)
        }, plan.Placements);
    }

    [Fact]
    public void Recycling_overflow_is_sent_to_the_deposit_cassette()
    {
        var cassettes = Cassettes(tryFiftyCapacity: 10, tryFiftyPhysical: 9);

        var plan = _planner.Plan(cassettes, [new(Currency.TRY, BanknoteDenomination.Fifty, 4)]);

        Assert.Equal(new[]
        {
            new DepositPlacementResponse(3, Currency.TRY, BanknoteDenomination.Fifty, 1),
            new DepositPlacementResponse(1, Currency.TRY, BanknoteDenomination.Fifty, 3)
        }, plan.Placements);
        Assert.Empty(plan.CapacityReturns);
    }

    [Fact]
    public void Full_deposit_cassette_returns_unaccepted_overflow()
    {
        var cassettes = Cassettes(depositCapacity: 3, depositPhysical: 2);

        var plan = _planner.Plan(cassettes, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 4)]);

        Assert.Equal(new DepositPlacementResponse(1, Currency.TRY, BanknoteDenomination.FiveHundred, 1), Assert.Single(plan.Placements));
        Assert.Equal(new DepositCapacityReturnResponse(Currency.TRY, BanknoteDenomination.FiveHundred, 3), Assert.Single(plan.CapacityReturns));
    }

    [Fact]
    public void When_every_cassette_is_full_all_notes_are_returned()
    {
        var cassettes = Cassettes(
            depositCapacity: 1,
            depositPhysical: 1,
            tryHundredCapacity: 1,
            tryHundredPhysical: 1,
            tryFiftyCapacity: 1,
            tryFiftyPhysical: 1,
            usdTwentyCapacity: 1,
            usdTwentyPhysical: 1,
            eurHundredCapacity: 1,
            eurHundredPhysical: 1);

        var plan = _planner.Plan(cassettes, [
            new(Currency.TRY, BanknoteDenomination.OneHundred, 1),
            new(Currency.USD, BanknoteDenomination.FiveHundred, 2)
        ]);

        Assert.Empty(plan.Placements);
        Assert.Equal(new[]
        {
            new DepositCapacityReturnResponse(Currency.TRY, BanknoteDenomination.OneHundred, 1),
            new DepositCapacityReturnResponse(Currency.USD, BanknoteDenomination.FiveHundred, 2)
        }, plan.CapacityReturns);
    }

    [Fact]
    public void Mixed_currency_notes_share_deposit_capacity_without_merging_types()
    {
        var plan = _planner.Plan(Cassettes(), [
            new(Currency.EUR, BanknoteDenomination.Fifty, 2),
            new(Currency.TRY, BanknoteDenomination.FiveHundred, 1)
        ]);

        Assert.Equal(new[]
        {
            new DepositPlacementResponse(1, Currency.TRY, BanknoteDenomination.FiveHundred, 1),
            new DepositPlacementResponse(1, Currency.EUR, BanknoteDenomination.Fifty, 2)
        }, plan.Placements);
    }

    [Fact]
    public void Negative_deposit_note_count_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            _planner.Plan(Cassettes(), [new(Currency.TRY, BanknoteDenomination.Twenty, -1)]));
    }

    private static CassetteCapacitySnapshot[] Cassettes(
        int depositCapacity = 100,
        int depositPhysical = 0,
        int tryHundredCapacity = 100,
        int tryHundredPhysical = 0,
        int tryFiftyCapacity = 100,
        int tryFiftyPhysical = 0,
        int usdTwentyCapacity = 100,
        int usdTwentyPhysical = 0,
        int eurHundredCapacity = 100,
        int eurHundredPhysical = 0,
        bool duplicateTryHundred = false,
        int firstDuplicateCapacity = 100,
        int secondDuplicateCapacity = 100) =>
    [
        new(1, CassetteType.DepositOnly, depositCapacity, null, null, depositPhysical),
        new(2, CassetteType.Recycling, duplicateTryHundred ? firstDuplicateCapacity : tryHundredCapacity,
            Currency.TRY, BanknoteDenomination.OneHundred,
            duplicateTryHundred ? 0 : tryHundredPhysical),
        new(3, CassetteType.Recycling, tryFiftyCapacity, Currency.TRY, BanknoteDenomination.Fifty, tryFiftyPhysical),
        new(4, CassetteType.Recycling, usdTwentyCapacity, Currency.USD, BanknoteDenomination.Twenty, usdTwentyPhysical),
        new(5, CassetteType.Recycling, duplicateTryHundred ? secondDuplicateCapacity : eurHundredCapacity,
            duplicateTryHundred ? Currency.TRY : Currency.EUR,
            duplicateTryHundred ? BanknoteDenomination.OneHundred : BanknoteDenomination.OneHundred,
            duplicateTryHundred ? 0 : eurHundredPhysical)
    ];
}
