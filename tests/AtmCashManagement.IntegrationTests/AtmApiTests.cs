using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.IntegrationTests;

public sealed class AtmApiTests(AtmApiFixture fixture) : IClassFixture<AtmApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public async Task End_to_end_flow_creates_configures_loads_services_and_reports_independent_atms()
    {
        await fixture.ResetDatabaseAsync();

        var atmId = await CreateAtmAsync("ATM-E2E-01");
        using var getAtm = await fixture.Client.GetAsync($"/api/atms/{atmId}");
        getAtm.EnsureSuccessStatusCode();
        Assert.Equal("UNCONFIGURED", await ReadStringAsync(getAtm, "state"));

        var configuration = ValidConfiguration(duplicateTryHundred: true);
        using var configure = await PostAsync($"/api/atms/{atmId}/configure", configuration);
        configure.EnsureSuccessStatusCode();
        Assert.Equal("CONFIGURED", await ReadStringAsync(configure, "state"));
        Assert.Equal(5, await ReadArrayLengthAsync(configure, "cassettes"));

        var initialLoad = InitialLoad(
            (1, [new(Currency.USD, BanknoteDenomination.Twenty, 2), new(Currency.EUR, BanknoteDenomination.OneHundred, 1)]),
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 10)]),
            (3, [new(Currency.TRY, BanknoteDenomination.Fifty, 20)]),
            (4, [new(Currency.USD, BanknoteDenomination.Twenty, 30)]),
            (5, [new(Currency.TRY, BanknoteDenomination.OneHundred, 5)]));
        using var load = await PostAsync($"/api/atms/{atmId}/load", initialLoad);
        load.EnsureSuccessStatusCode();
        Assert.Equal("LOADED", await ReadStringAsync(load, "state"));
        Assert.Equal(6, await fixture.GetInitialLoadMovementCountAsync());

        using var start = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.InService));
        start.EnsureSuccessStatusCode();
        Assert.Equal("IN_SERVICE", await ReadStringAsync(start, "state"));

        using var balances = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        balances.EnsureSuccessStatusCode();
        using var balancesDocument = await JsonDocument.ParseAsync(await balances.Content.ReadAsStreamAsync());
        var root = balancesDocument.RootElement;
        Assert.Equal("IN_SERVICE", root.GetProperty("state").GetString());
        Assert.Equal(5, root.GetProperty("cassettes").GetArrayLength());
        Assert.Equal(2, root.GetProperty("cassettes")[0].GetProperty("balances").GetArrayLength());
        Assert.Equal(2_500, GetCurrencyTotal(root, "TRY"));
        Assert.Equal(640, GetCurrencyTotal(root, "USD"));
        Assert.Equal(100, GetCurrencyTotal(root, "EUR"));

        var secondAtmId = await CreateAtmAsync("ATM-E2E-02");
        using var secondBalances = await fixture.Client.GetAsync($"/api/atms/{secondAtmId}/balances");
        secondBalances.EnsureSuccessStatusCode();
        using var secondDocument = await ParseAsync(secondBalances);
        Assert.Equal(0, GetCurrencyTotal(secondDocument.RootElement, "TRY"));

        using var allAtms = await fixture.Client.GetAsync("/api/atms");
        allAtms.EnsureSuccessStatusCode();
        Assert.Equal(2, await ReadTopLevelArrayLengthAsync(allAtms));
    }

    [Fact]
    public async Task Configure_rejects_missing_cassettes_wrong_deposit_count_and_foreign_currency_count()
    {
        await fixture.ResetDatabaseAsync();
        var atmId = await CreateAtmAsync("ATM-CONFIG-01");

        var invalidCurrencyJson = JsonSerializer.Serialize(ValidConfiguration(), JsonOptions)
            .Replace("\"USD\"", "\"GBP\"", StringComparison.Ordinal);
        using var invalidCurrency = await PostRawAsync($"/api/atms/{atmId}/configure", invalidCurrencyJson);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCurrency.StatusCode);
        Assert.Equal("application/problem+json", invalidCurrency.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bad_request", await ReadProblemCodeAsync(invalidCurrency));

        var invalidDenominationJson = JsonSerializer.Serialize(ValidConfiguration(), JsonOptions)
            .Replace("\"100\"", "\"25\"", StringComparison.Ordinal);
        using var invalidDenomination = await PostRawAsync($"/api/atms/{atmId}/configure", invalidDenominationJson);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDenomination.StatusCode);

        using var missingCassette = await PostAsync($"/api/atms/{atmId}/configure", new ConfigureAtmRequest(ValidConfiguration().Cassettes!.Take(4).ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, missingCassette.StatusCode);
        Assert.Equal("cassette_count_invalid", await ReadProblemCodeAsync(missingCassette));

        var noForeign = ValidConfiguration().Cassettes!.Select(cassette =>
            cassette with { Currency = cassette.Type == CassetteType.Recycling ? Currency.TRY : null }).ToArray();
        using var noForeignResponse = await PostAsync($"/api/atms/{atmId}/configure", new ConfigureAtmRequest(noForeign));
        Assert.Equal(HttpStatusCode.BadRequest, noForeignResponse.StatusCode);
        Assert.Equal("foreign_cassette_count_invalid", await ReadProblemCodeAsync(noForeignResponse));

        var twoDeposits = ValidConfiguration().Cassettes!.Select(cassette => cassette with
        {
            Type = cassette.Number == 2 ? CassetteType.DepositOnly : cassette.Type,
            Currency = cassette.Number == 2 ? null : cassette.Currency,
            Denomination = cassette.Number == 2 ? null : cassette.Denomination
        }).ToArray();
        using var wrongTypeCount = await PostAsync($"/api/atms/{atmId}/configure", new ConfigureAtmRequest(twoDeposits));
        Assert.Equal(HttpStatusCode.BadRequest, wrongTypeCount.StatusCode);
        Assert.Equal("cassette_type_count_invalid", await ReadProblemCodeAsync(wrongTypeCount));

        using var getAtm = await fixture.Client.GetAsync($"/api/atms/{atmId}");
        Assert.Equal("UNCONFIGURED", await ReadStringAsync(getAtm, "state"));
        Assert.Equal(0, await ReadArrayLengthAsync(getAtm, "cassettes"));
    }

    [Fact]
    public async Task Load_rejects_wrong_recycling_notes_and_failed_multi_cassette_load_is_atomic()
    {
        await fixture.ResetDatabaseAsync();
        var atmId = await CreateAtmAsync("ATM-ATOMIC-01");
        using var configure = await PostAsync($"/api/atms/{atmId}/configure", ValidConfiguration());
        configure.EnsureSuccessStatusCode();

        var wrongRecyclingNote = InitialLoad(
            (1, []),
            (2, [new(Currency.TRY, BanknoteDenomination.Fifty, 1)]),
            (3, []), (4, []), (5, []));
        using var mismatch = await PostAsync($"/api/atms/{atmId}/load", wrongRecyclingNote);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal("recycling_note_mismatch", await ReadProblemCodeAsync(mismatch));

        var overCapacity = InitialLoad(
            (1, []),
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]),
            (3, [new(Currency.TRY, BanknoteDenomination.Fifty, 101)]),
            (4, []), (5, []));
        using var failedLoad = await PostAsync($"/api/atms/{atmId}/load", overCapacity);
        Assert.Equal(HttpStatusCode.Conflict, failedLoad.StatusCode);
        Assert.Equal("cassette_capacity_exceeded", await ReadProblemCodeAsync(failedLoad));

        using var balances = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var document = await ParseAsync(balances);
        Assert.Equal("CONFIGURED", document.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("cassettes").EnumerateArray()
            .Sum(cassette => cassette.GetProperty("physicalCount").GetInt32()));
        Assert.Equal(0, await fixture.GetInitialLoadMovementCountAsync());
    }

    [Fact]
    public async Task Initial_load_accepts_capacity_of_one_hundred_but_rejects_overfill_without_changes()
    {
        await fixture.ResetDatabaseAsync();
        var atmId = await CreateAtmAsync("ATM-CAPACITY-01");
        using var configure = await PostAsync($"/api/atms/{atmId}/configure", ValidConfiguration());
        configure.EnsureSuccessStatusCode();

        using var load = await PostAsync($"/api/atms/{atmId}/load", InitialLoad(
            (1, []),
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 100)]),
            (3, []), (4, []), (5, [])));
        load.EnsureSuccessStatusCode();
        Assert.Equal("LOADED", await ReadStringAsync(load, "state"));

        using var inService = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.InService));
        inService.EnsureSuccessStatusCode();
        using var maintenance = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.Maintenance));
        maintenance.EnsureSuccessStatusCode();

        using var overfill = await PostAsync($"/api/atms/{atmId}/load", new LoadCashRequest(
        [
            new CassetteLoadRequest(2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])
        ]));
        Assert.Equal(HttpStatusCode.Conflict, overfill.StatusCode);
        Assert.Equal("cassette_capacity_exceeded", await ReadProblemCodeAsync(overfill));

        using var balances = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var document = await ParseAsync(balances);
        Assert.Equal(100, document.RootElement.GetProperty("cassettes")[1].GetProperty("physicalCount").GetInt32());
        Assert.Equal(1, await fixture.GetInitialLoadMovementCountAsync());
    }

    [Fact]
    public async Task Lifecycle_and_configuration_are_guarded_by_state_and_existing_cash()
    {
        await fixture.ResetDatabaseAsync();
        var atmId = await CreateAtmAsync("ATM-STATE-01");

        using var wrongStateLoad = await PostAsync($"/api/atms/{atmId}/load", InitialLoad((1, []), (2, []), (3, []), (4, []), (5, [])));
        Assert.Equal(HttpStatusCode.Conflict, wrongStateLoad.StatusCode);
        Assert.Equal("invalid_atm_state", await ReadProblemCodeAsync(wrongStateLoad));

        using var configure = await PostAsync($"/api/atms/{atmId}/configure", ValidConfiguration());
        configure.EnsureSuccessStatusCode();
        using var prematureLoaded = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.Loaded));
        Assert.Equal(HttpStatusCode.Conflict, prematureLoaded.StatusCode);

        using var load = await PostAsync($"/api/atms/{atmId}/load", InitialLoad(
            (1, []), (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 5)]),
            (3, []), (4, []), (5, [])));
        load.EnsureSuccessStatusCode();
        using var service = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.InService));
        service.EnsureSuccessStatusCode();

        var changedCashCassette = ValidConfiguration().Cassettes!.Select(cassette =>
            cassette.Number == 2
                ? cassette with { Denomination = BanknoteDenomination.FiveHundred }
                : cassette).ToArray();
        using var configureWhileInService = await PostAsync($"/api/atms/{atmId}/configure", new ConfigureAtmRequest(changedCashCassette));
        Assert.Equal(HttpStatusCode.Conflict, configureWhileInService.StatusCode);

        using var maintenance = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.Maintenance));
        maintenance.EnsureSuccessStatusCode();
        using var reconfigureWithCash = await PostAsync($"/api/atms/{atmId}/configure", new ConfigureAtmRequest(changedCashCassette));
        Assert.Equal(HttpStatusCode.Conflict, reconfigureWithCash.StatusCode);
        Assert.Equal("cassette_contains_cash", await ReadProblemCodeAsync(reconfigureWithCash));

        using var unchangedMaintenanceConfig = await PostAsync($"/api/atms/{atmId}/configure", ValidConfiguration());
        unchangedMaintenanceConfig.EnsureSuccessStatusCode();
        Assert.Equal("MAINTENANCE", await ReadStringAsync(unchangedMaintenanceConfig, "state"));

        using var backInService = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.InService));
        backInService.EnsureSuccessStatusCode();
        using var invalidTransition = await PostAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(AtmLifecycleState.Unconfigured));
        Assert.Equal(HttpStatusCode.Conflict, invalidTransition.StatusCode);
        Assert.Equal("invalid_state_transition", await ReadProblemCodeAsync(invalidTransition));
    }

    private async Task<Guid> CreateAtmAsync(string code)
    {
        using var response = await PostAsync("/api/atms", new CreateAtmRequest(code));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ParseAsync(response);
        Assert.Equal("UNCONFIGURED", document.RootElement.GetProperty("state").GetString());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PostAsync(string path, object body) =>
        fixture.Client.PostAsJsonAsync(path, body, JsonOptions);

    private Task<HttpResponseMessage> PostRawAsync(string path, string json) =>
        fixture.Client.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    private static ConfigureAtmRequest ValidConfiguration(bool duplicateTryHundred = false) =>
        new(
        [
            new CassetteDefinitionRequest(1, CassetteType.DepositOnly),
            new CassetteDefinitionRequest(2, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.OneHundred),
            new CassetteDefinitionRequest(3, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.Fifty),
            new CassetteDefinitionRequest(4, CassetteType.Recycling, Currency.USD, BanknoteDenomination.Twenty),
            new CassetteDefinitionRequest(
                5,
                CassetteType.Recycling,
                Currency.TRY,
                duplicateTryHundred ? BanknoteDenomination.OneHundred : BanknoteDenomination.Twenty)
        ]);

    private static LoadCashRequest InitialLoad(params (int CassetteNumber, BanknoteLoadRequest[] Notes)[] cassettes) =>
        new(cassettes.Select(cassette => new CassetteLoadRequest(cassette.CassetteNumber, cassette.Notes)).ToArray());

    private static async Task<string?> ReadStringAsync(HttpResponseMessage response, string property)
    {
        using var document = await ParseAsync(response);
        return document.RootElement.GetProperty(property).GetString();
    }

    private static async Task<int> ReadArrayLengthAsync(HttpResponseMessage response, string property)
    {
        using var document = await ParseAsync(response);
        return document.RootElement.GetProperty(property).GetArrayLength();
    }

    private static async Task<int> ReadTopLevelArrayLengthAsync(HttpResponseMessage response)
    {
        using var document = await ParseAsync(response);
        return document.RootElement.GetArrayLength();
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = await ParseAsync(response);
        return document.RootElement.TryGetProperty("errorCode", out var value) ? value.GetString() : null;
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static long GetCurrencyTotal(JsonElement root, string currency) =>
        root.GetProperty("totalsByCurrency").EnumerateArray()
            .Single(total => total.GetProperty("currency").GetString() == currency)
            .GetProperty("totalValue").GetInt64();
}
