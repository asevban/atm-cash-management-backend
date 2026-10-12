using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application;

public static class CassetteConfigurationValidator
{
    public static IReadOnlyList<CassetteDefinitionRequest> Validate(ConfigureAtmRequest request)
    {
        if (request is null)
        {
            throw AtmApiException.Validation("configuration_required", "A cassette configuration request is required.");
        }

        var cassettes = request.Cassettes;
        if (cassettes is null || cassettes.Count != 5)
        {
            throw AtmApiException.Validation("cassette_count_invalid", "Exactly five cassettes must be configured in one request.");
        }

        if (cassettes.Any(cassette => cassette is null))
        {
            throw AtmApiException.Validation("cassette_invalid", "Cassette definitions cannot be null.");
        }

        if (cassettes.Any(cassette => cassette.Number is < 1 or > 5) ||
            cassettes.Select(cassette => cassette.Number).Distinct().Count() != 5)
        {
            throw AtmApiException.Validation("cassette_slot_invalid", "Cassette numbers must be unique and cover slots 1 through 5.");
        }

        if (cassettes.Any(cassette => !Enum.IsDefined(cassette.Type)))
        {
            throw AtmApiException.Validation("cassette_type_invalid", "Cassette type is not supported.");
        }

        if (cassettes.Any(cassette => cassette.Capacity is < 1 or > 100))
        {
            throw AtmApiException.Validation("cassette_capacity_invalid", "Each cassette capacity must be between 1 and 100 banknotes.");
        }

        var depositOnly = cassettes.Where(cassette => cassette.Type == CassetteType.DepositOnly).ToArray();
        var recycling = cassettes.Where(cassette => cassette.Type == CassetteType.Recycling).ToArray();
        if (depositOnly.Length != 1 || recycling.Length != 4)
        {
            throw AtmApiException.Validation("cassette_type_count_invalid", "Configuration requires exactly one DEPOSIT_ONLY and four RECYCLING cassettes.");
        }

        if (depositOnly[0].Currency is not null || depositOnly[0].Denomination is not null)
        {
            throw AtmApiException.Validation("deposit_cassette_restricted", "A DEPOSIT_ONLY cassette cannot be restricted to a currency or denomination.");
        }

        if (recycling.Any(cassette =>
                cassette.Currency is null ||
                !Enum.IsDefined(cassette.Currency.Value) ||
                cassette.Denomination is null ||
                !Enum.IsDefined(cassette.Denomination.Value)))
        {
            throw AtmApiException.Validation("recycling_configuration_invalid", "Each RECYCLING cassette requires a supported currency and denomination.");
        }

        var foreignCurrencyCassettes = recycling.Count(cassette => cassette.Currency is Currency.USD or Currency.EUR);
        if (foreignCurrencyCassettes is < 1 or > 2)
        {
            throw AtmApiException.Validation("foreign_cassette_count_invalid", "One or two RECYCLING cassettes must use USD or EUR.");
        }

        return cassettes.OrderBy(cassette => cassette.Number).ToArray();
    }

    public static void ValidateStored(IReadOnlyCollection<Domain.Entities.Cassette> cassettes)
    {
        try
        {
            Validate(new ConfigureAtmRequest(cassettes
                .Select(cassette => new CassetteDefinitionRequest(
                    cassette.Number,
                    cassette.Type,
                    cassette.Currency,
                    cassette.Denomination,
                    cassette.Capacity))
                .ToArray()));
        }
        catch (AtmApiException exception)
        {
            throw AtmApiException.Conflict("atm_configuration_invalid", exception.Message);
        }
    }
}
