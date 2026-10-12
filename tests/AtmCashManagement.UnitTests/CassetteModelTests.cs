using System.Text.Json;
using System.Text.Json.Serialization;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.UnitTests;

public sealed class CassetteModelTests
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Deposit_only_cassette_is_not_restricted_to_one_currency_or_denomination()
    {
        var cassette = new Cassette(Guid.NewGuid(), 5, CassetteType.DepositOnly);

        Assert.Null(cassette.Currency);
        Assert.Null(cassette.Denomination);
        Assert.Equal(100, cassette.Capacity);
    }

    [Fact]
    public void Recycling_cassette_requires_a_currency_and_denomination()
    {
        Assert.Throws<ArgumentException>(() =>
            new Cassette(Guid.NewGuid(), 1, CassetteType.Recycling));
    }

    [Fact]
    public void Available_count_excludes_reserved_notes()
    {
        var balance = new CassetteBalance(
            Guid.NewGuid(),
            Currency.TRY,
            BanknoteDenomination.Fifty,
            physicalCount: 18,
            reservedCount: 4);

        Assert.Equal(14, balance.AvailableCount);
    }

    [Fact]
    public void Reserved_count_cannot_exceed_physical_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CassetteBalance(
                Guid.NewGuid(),
                Currency.USD,
                BanknoteDenomination.OneHundred,
                physicalCount: 5,
                reservedCount: 6));
    }

    [Fact]
    public void Lifecycle_state_uses_the_assignment_api_value()
    {
        Assert.Equal("\"IN_SERVICE\"", JsonSerializer.Serialize(AtmLifecycleState.InService, ApiJsonOptions));
    }

    [Fact]
    public void Denomination_serializes_as_the_banknote_value()
    {
        Assert.Equal("\"100\"", JsonSerializer.Serialize(BanknoteDenomination.OneHundred, ApiJsonOptions));
    }
}
