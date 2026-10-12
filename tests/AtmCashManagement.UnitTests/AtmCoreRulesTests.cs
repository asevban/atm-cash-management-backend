using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.UnitTests;

public sealed class AtmCoreRulesTests
{
    [Fact]
    public void New_atm_starts_unconfigured()
    {
        var atm = new Atm("ATM-UNIT-01");

        Assert.Equal(AtmLifecycleState.Unconfigured, atm.State);
    }

    [Fact]
    public void Configuration_accepts_five_slots_and_one_or_two_foreign_recycling_cassettes()
    {
        var definitions = CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(ValidCassettes()));

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, definitions.Select(cassette => cassette.Number));
    }

    [Fact]
    public void Configuration_rejects_any_count_other_than_five()
    {
        var exception = Assert.Throws<AtmApiException>(() =>
            CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(ValidCassettes().Take(4).ToArray())));

        Assert.Equal("cassette_count_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Configuration_requires_exactly_one_deposit_only_cassette()
    {
        var definitions = ValidCassettes();
        definitions[1] = definitions[1] with { Type = CassetteType.DepositOnly, Currency = null, Denomination = null };

        var exception = Assert.Throws<AtmApiException>(() =>
            CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(definitions)));

        Assert.Equal("cassette_type_count_invalid", exception.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Configuration_requires_one_or_two_foreign_currency_cassettes(int foreignCount)
    {
        var definitions = ValidCassettes();
        var recycling = definitions.Where(item => item.Type == CassetteType.Recycling).ToArray();
        foreach (var cassette in recycling)
        {
            var index = definitions.IndexOf(cassette);
            definitions[index] = cassette with { Currency = Currency.TRY };
        }

        for (var index = 0; index < foreignCount; index++)
        {
            var definitionIndex = definitions.FindIndex(item => item.Type == CassetteType.Recycling && item.Currency == Currency.TRY);
            definitions[definitionIndex] = definitions[definitionIndex] with { Currency = Currency.USD };
        }

        var exception = Assert.Throws<AtmApiException>(() =>
            CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(definitions)));

        Assert.Equal("foreign_cassette_count_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Configuration_rejects_unsupported_currency_and_denomination_values()
    {
        var unsupportedCurrency = ValidCassettes();
        unsupportedCurrency[3] = unsupportedCurrency[3] with { Currency = (Currency)99 };
        Assert.Equal("recycling_configuration_invalid", Assert.Throws<AtmApiException>(() =>
            CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(unsupportedCurrency))).ErrorCode);

        var unsupportedDenomination = ValidCassettes();
        unsupportedDenomination[3] = unsupportedDenomination[3] with { Denomination = (BanknoteDenomination)25 };
        Assert.Equal("recycling_configuration_invalid", Assert.Throws<AtmApiException>(() =>
            CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(unsupportedDenomination))).ErrorCode);
    }

    [Fact]
    public void Configuration_allows_duplicate_recycling_currency_and_denomination()
    {
        var definitions = ValidCassettes();
        definitions[4] = definitions[4] with { Currency = Currency.TRY, Denomination = BanknoteDenomination.OneHundred };

        var result = CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(definitions));

        Assert.Equal(2, result.Count(item =>
            item.Type == CassetteType.Recycling &&
            item.Currency == Currency.TRY &&
            item.Denomination == BanknoteDenomination.OneHundred));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Configuration_rejects_capacity_outside_one_to_one_hundred(int capacity)
    {
        var definitions = ValidCassettes();
        definitions[0] = definitions[0] with { Capacity = capacity };

        Assert.Equal("cassette_capacity_invalid", Assert.Throws<AtmApiException>(() =>
            CassetteConfigurationValidator.Validate(new ConfigureAtmRequest(definitions))).ErrorCode);
    }

    [Fact]
    public void Cassette_balance_allows_exactly_one_hundred_physical_notes()
    {
        var cassetteId = Guid.NewGuid();
        var balance = new CassetteBalance(cassetteId, Currency.TRY, BanknoteDenomination.Twenty, physicalCount: 100);

        Assert.Equal(100, balance.PhysicalCount);
    }

    [Fact]
    public void Cassette_balance_available_count_subtracts_reserved_notes()
    {
        var balance = new CassetteBalance(Guid.NewGuid(), Currency.TRY, BanknoteDenomination.Fifty, 18, 4);

        Assert.Equal(14, balance.AvailableCount);
    }

    [Theory]
    [InlineData(AtmLifecycleState.Unconfigured, AtmLifecycleState.Configured, true)]
    [InlineData(AtmLifecycleState.Configured, AtmLifecycleState.Loaded, true)]
    [InlineData(AtmLifecycleState.Loaded, AtmLifecycleState.InService, true)]
    [InlineData(AtmLifecycleState.InService, AtmLifecycleState.Maintenance, true)]
    [InlineData(AtmLifecycleState.Maintenance, AtmLifecycleState.InService, true)]
    [InlineData(AtmLifecycleState.Unconfigured, AtmLifecycleState.InService, false)]
    [InlineData(AtmLifecycleState.Loaded, AtmLifecycleState.Maintenance, false)]
    [InlineData(AtmLifecycleState.Maintenance, AtmLifecycleState.Loaded, false)]
    public void Lifecycle_allows_only_required_transitions(AtmLifecycleState current, AtmLifecycleState target, bool expected)
    {
        Assert.Equal(expected, AtmLifecycleRules.IsValidTransition(current, target));
    }

    [Theory]
    [InlineData(AtmLifecycleState.Unconfigured, false, false, false)]
    [InlineData(AtmLifecycleState.Configured, false, true, false)]
    [InlineData(AtmLifecycleState.Loaded, false, false, false)]
    [InlineData(AtmLifecycleState.InService, true, false, false)]
    [InlineData(AtmLifecycleState.Maintenance, false, true, true)]
    public void Operation_state_rules_are_centralized(
        AtmLifecycleState state,
        bool customerCash,
        bool loadCash,
        bool reconcile)
    {
        Assert.Equal(customerCash, AtmLifecycleRules.CanPerformCustomerCashOperation(state));
        Assert.Equal(loadCash, AtmLifecycleRules.CanLoadCash(state));
        Assert.Equal(reconcile, AtmLifecycleRules.CanReconcile(state));
    }

    private static List<CassetteDefinitionRequest> ValidCassettes() =>
    [
        new(1, CassetteType.DepositOnly),
        new(2, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.OneHundred),
        new(3, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.Fifty),
        new(4, CassetteType.Recycling, Currency.USD, BanknoteDenomination.Twenty),
        new(5, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.Twenty)
    ];
}
