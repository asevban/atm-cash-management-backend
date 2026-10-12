using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtmCashManagement.IntegrationTests;

public sealed class AtmDepositReconciliationTests(AtmApiFixture fixture) : IClassFixture<AtmApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public async Task Deposit_routes_to_recycling_persists_ledger_and_updates_balances()
    {
        var atmId = await CreateAtmAsync(inService: true);
        using var response = await PostDepositAsync(atmId,
            [new(Currency.TRY, BanknoteDenomination.Fifty, 2)], unrecognized: 2);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = await ParseAsync(response);
        var root = result.RootElement;
        Assert.Equal(2, root.GetProperty("acceptedNotes")[0].GetProperty("count").GetInt32());
        Assert.Equal(3, root.GetProperty("placements")[0].GetProperty("cassetteNumber").GetInt32());
        Assert.Equal(2, root.GetProperty("unrecognizedNoteCount").GetInt32());
        Assert.Empty(root.GetProperty("capacityReturns").EnumerateArray());
        Assert.Equal(100, GetCurrencyValue(root, "acceptedValueByCurrency", "TRY", "totalValue"));

        var transactionId = root.GetProperty("transactionId").GetGuid();
        var deposit = await GetDepositAsync(transactionId);
        Assert.Equal(2, deposit.UnrecognizedNoteCount);
        var depositLine = Assert.Single(deposit.Notes);
        Assert.Equal(2, depositLine.AcceptedCount);
        Assert.Equal(0, depositLine.ReturnedCount);

        var movements = await GetMovementsAsync(atmId, CassetteMovementType.Deposit);
        var movement = Assert.Single(movements);
        Assert.Equal(3, movement.Cassette.Number);
        Assert.Equal(2, movement.PhysicalCountDelta);
        Assert.Equal(transactionId.ToString("N"), movement.TransactionReference);

        using var balancesResponse = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var balances = await ParseAsync(balancesResponse);
        Assert.Equal(2, GetCassetteBalanceCount(balances.RootElement, 3, "TRY", "50", "physicalCount"));
        Assert.Equal(100, GetCurrencyValue(balances.RootElement, "totalsByCurrency", "TRY", "totalValue"));
    }

    [Fact]
    public async Task Deposit_sends_unmatched_banknotes_to_the_deposit_only_cassette()
    {
        var atmId = await CreateAtmAsync(inService: true);
        using var response = await PostDepositAsync(atmId, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 2)]);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var placement = Assert.Single(result.RootElement.GetProperty("placements").EnumerateArray());
        Assert.Equal(1, placement.GetProperty("cassetteNumber").GetInt32());
        Assert.Equal(2, placement.GetProperty("count").GetInt32());

        using var balancesResponse = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var balances = await ParseAsync(balancesResponse);
        Assert.Equal(2, GetCassetteBalanceCount(balances.RootElement, 1, "TRY", "500", "physicalCount"));
    }

    [Fact]
    public async Task Concurrent_deposits_do_not_exceed_shared_cassette_capacity()
    {
        var atmId = await CreateAtmAsync(
            inService: true,
            configuration: Configuration(depositCapacity: 1));

        var responses = await Task.WhenAll(
            PostDepositAsync(atmId, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 1)]),
            PostDepositAsync(atmId, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 1)]));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var results = await Task.WhenAll(responses.Select(ParseAsync));
            try
            {
                var accepted = results.Sum(result => result.RootElement.GetProperty("acceptedNotes").EnumerateArray()
                    .Sum(note => note.GetProperty("count").GetInt32()));
                var returned = results.Sum(result => result.RootElement.GetProperty("capacityReturns").EnumerateArray()
                    .Sum(note => note.GetProperty("count").GetInt32()));

                Assert.Equal(1, accepted);
                Assert.Equal(1, returned);
                Assert.Single(await GetMovementsAsync(atmId, CassetteMovementType.Deposit));
                Assert.Equal(2, await CountDepositsAsync(atmId));

                using var balancesResponse = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
                using var balances = await ParseAsync(balancesResponse);
                Assert.Equal(1, GetCassetteBalanceCount(balances.RootElement, 1, "TRY", "500", "physicalCount"));
            }
            finally
            {
                foreach (var result in results)
                {
                    result.Dispose();
                }
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Duplicate_recycling_cassettes_are_filled_by_slot_order_then_overflow_to_deposit()
    {
        var configuration = Configuration(duplicateTryHundred: true, tryHundredCapacity: 2, slotFiveCapacity: 5);
        var atmId = await CreateAtmAsync(inService: true, configuration: configuration);
        using var response = await PostDepositAsync(atmId, [new(Currency.TRY, BanknoteDenomination.OneHundred, 9)]);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { (2, 2), (5, 5), (1, 2) }, result.RootElement.GetProperty("placements")
            .EnumerateArray()
            .Select(item => (item.GetProperty("cassetteNumber").GetInt32(), item.GetProperty("count").GetInt32()))
            .ToArray());
    }

    [Fact]
    public async Task Recycling_capacity_overflow_is_placed_in_deposit_cassette()
    {
        var initial = InitialCounts((3, [new(Currency.TRY, BanknoteDenomination.Fifty, 9)]));
        var atmId = await CreateAtmAsync(inService: true, configuration: Configuration(tryFiftyCapacity: 10), initialCounts: initial);
        using var response = await PostDepositAsync(atmId, [new(Currency.TRY, BanknoteDenomination.Fifty, 4)]);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { (3, 1), (1, 3) }, result.RootElement.GetProperty("placements")
            .EnumerateArray()
            .Select(item => (item.GetProperty("cassetteNumber").GetInt32(), item.GetProperty("count").GetInt32()))
            .ToArray());
        Assert.Empty(result.RootElement.GetProperty("capacityReturns").EnumerateArray());
    }

    [Fact]
    public async Task Full_deposit_cassette_returns_only_the_unaccepted_overflow()
    {
        var initial = InitialCounts((1, [new(Currency.USD, BanknoteDenomination.Twenty, 2)]));
        var atmId = await CreateAtmAsync(inService: true, configuration: Configuration(depositCapacity: 3), initialCounts: initial);
        using var response = await PostDepositAsync(atmId, [new(Currency.EUR, BanknoteDenomination.Fifty, 4)]);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new DepositPlacementResponse(1, Currency.EUR, BanknoteDenomination.Fifty, 1),
            JsonSerializer.Deserialize<DepositPlacementResponse>(result.RootElement.GetProperty("placements")[0].GetRawText(), JsonOptions));
        Assert.Equal(new DepositCapacityReturnResponse(Currency.EUR, BanknoteDenomination.Fifty, 3),
            JsonSerializer.Deserialize<DepositCapacityReturnResponse>(result.RootElement.GetProperty("capacityReturns")[0].GetRawText(), JsonOptions));

        var deposit = await GetDepositAsync(result.RootElement.GetProperty("transactionId").GetGuid());
        var line = Assert.Single(deposit.Notes);
        Assert.Equal(1, line.AcceptedCount);
        Assert.Equal(3, line.ReturnedCount);
    }

    [Fact]
    public async Task When_all_cassettes_are_full_all_recognized_notes_are_returned_without_movements()
    {
        var initial = InitialCounts(
            (1, [new(Currency.USD, BanknoteDenomination.Fifty, 1)]),
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]),
            (3, [new(Currency.TRY, BanknoteDenomination.Fifty, 1)]),
            (4, [new(Currency.USD, BanknoteDenomination.Twenty, 1)]),
            (5, [new(Currency.EUR, BanknoteDenomination.OneHundred, 1)]));
        var configuration = Configuration(
            depositCapacity: 1,
            tryHundredCapacity: 1,
            tryFiftyCapacity: 1,
            usdTwentyCapacity: 1,
            slotFiveCapacity: 1);
        var atmId = await CreateAtmAsync(inService: true, configuration: configuration, initialCounts: initial);
        using var response = await PostDepositAsync(atmId,
        [
            new(Currency.TRY, BanknoteDenomination.OneHundred, 1),
            new(Currency.TRY, BanknoteDenomination.FiveHundred, 2)
        ]);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(result.RootElement.GetProperty("placements").EnumerateArray());
        Assert.Equal(new[] { 1, 2 }, result.RootElement.GetProperty("capacityReturns").EnumerateArray()
            .Select(item => item.GetProperty("count").GetInt32()).Order().ToArray());
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.Deposit));
    }

    [Fact]
    public async Task Unrecognized_notes_are_returned_separately_and_do_not_change_balances_or_movements()
    {
        var atmId = await CreateAtmAsync(inService: true);
        using var response = await PostDepositAsync(atmId, [], unrecognized: 3);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(result.RootElement.GetProperty("acceptedNotes").EnumerateArray());
        Assert.Empty(result.RootElement.GetProperty("placements").EnumerateArray());
        Assert.Equal(3, result.RootElement.GetProperty("unrecognizedNoteCount").GetInt32());
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.Deposit));

        using var balancesResponse = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var balances = await ParseAsync(balancesResponse);
        Assert.Equal(0, GetCurrencyValue(balances.RootElement, "totalsByCurrency", "TRY", "totalValue"));
        Assert.Equal(20, GetCurrencyValue(balances.RootElement, "totalsByCurrency", "USD", "totalValue"));
    }

    [Fact]
    public async Task Mixed_currency_deposit_reports_accepted_values_separately()
    {
        var atmId = await CreateAtmAsync(inService: true);
        using var response = await PostDepositAsync(atmId,
        [
            new(Currency.TRY, BanknoteDenomination.FiveHundred, 1),
            new(Currency.USD, BanknoteDenomination.Twenty, 2),
            new(Currency.EUR, BanknoteDenomination.Fifty, 3)
        ]);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500, GetCurrencyValue(result.RootElement, "acceptedValueByCurrency", "TRY", "totalValue"));
        Assert.Equal(40, GetCurrencyValue(result.RootElement, "acceptedValueByCurrency", "USD", "totalValue"));
        Assert.Equal(150, GetCurrencyValue(result.RootElement, "acceptedValueByCurrency", "EUR", "totalValue"));
        Assert.Equal(3, result.RootElement.GetProperty("placements").GetArrayLength());
    }

    [Theory]
    [InlineData("{\"notes\":[{\"currency\":\"GBP\",\"denomination\":\"20\",\"count\":1}],\"unrecognizedNoteCount\":0}")]
    [InlineData("{\"notes\":[{\"currency\":\"TRY\",\"denomination\":\"10\",\"count\":1}],\"unrecognizedNoteCount\":0}")]
    [InlineData("{\"notes\":[{\"currency\":\"TRY\",\"denomination\":\"20\",\"count\":-1}],\"unrecognizedNoteCount\":0}")]
    [InlineData("{\"notes\":[],\"unrecognizedNoteCount\":-1}")]
    public async Task Invalid_deposit_currency_denomination_or_count_is_rejected_atomically(string json)
    {
        var atmId = await CreateAtmAsync(inService: true);
        using var response = await fixture.Client.PostAsync(
            $"/api/atms/{atmId}/deposits",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountDepositsAsync(atmId));
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.Deposit));
    }

    [Fact]
    public async Task Deposit_is_rejected_outside_in_service()
    {
        var atmId = await CreateAtmAsync(inService: false);
        using var response = await PostDepositAsync(atmId, [new(Currency.TRY, BanknoteDenomination.Fifty, 1)]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountDepositsAsync(atmId));
    }

    [Fact]
    public async Task Invalid_deposit_does_not_save_a_header_balance_or_partial_movement()
    {
        var atmId = await CreateAtmAsync(inService: true);
        using var response = await PostDepositAsync(atmId,
        [
            new(Currency.TRY, BanknoteDenomination.Fifty, 2),
            new(Currency.TRY, BanknoteDenomination.Fifty, 1)
        ]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountDepositsAsync(atmId));
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.Deposit));
    }

    [Fact]
    public async Task Reconciliation_with_matching_physical_snapshot_records_no_adjustment()
    {
        var initial = InitialCounts((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 3)]));
        var atmId = await CreateAtmAsync(inService: true, initialCounts: initial);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);

        using var response = await PostReconciliationAsync(atmId,
            Snapshot((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 3)])));
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(result.RootElement.GetProperty("changedCassettes").EnumerateArray());
        Assert.Equal(0, GetCurrencyValue(result.RootElement, "differenceValueByCurrency", "TRY", "differenceValue"));
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.ReconciliationAdjustment));
        var record = await GetReconciliationAsync(result.RootElement.GetProperty("reconciliationId").GetGuid());
        Assert.Contains(record.Lines, line => line.Cassette.Number == 2 && line.ExpectedCount == 3 && line.CountedCount == 3);

        using var repeatResponse = await PostReconciliationAsync(atmId,
            Snapshot((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 3)])));
        Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.ReconciliationAdjustment));
    }

    [Fact]
    public async Task Reconciliation_records_missing_notes_as_negative_adjustment_and_updates_balance()
    {
        var initial = InitialCounts((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 3)]));
        var atmId = await CreateAtmAsync(inService: true, initialCounts: initial);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);

        using var response = await PostReconciliationAsync(atmId,
            Snapshot((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)])));
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(-100, GetCurrencyValue(result.RootElement, "differenceValueByCurrency", "TRY", "differenceValue"));
        var movement = Assert.Single(await GetMovementsAsync(atmId, CassetteMovementType.ReconciliationAdjustment));
        Assert.Equal(-1, movement.PhysicalCountDelta);
        Assert.Equal(result.RootElement.GetProperty("reconciliationId").GetGuid().ToString("N"), movement.TransactionReference);

        using var balancesResponse = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var balances = await ParseAsync(balancesResponse);
        Assert.Equal(2, GetCassetteBalanceCount(balances.RootElement, 2, "TRY", "100", "physicalCount"));
    }

    [Fact]
    public async Task Reconciliation_records_extra_mixed_deposit_only_notes_and_currency_differences()
    {
        var atmId = await CreateAtmAsync(inService: true);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);

        using var response = await PostReconciliationAsync(atmId, Snapshot(
            (1, [
                new(Currency.TRY, BanknoteDenomination.Fifty, 2),
                new(Currency.USD, BanknoteDenomination.Fifty, 3)
            ])));
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, GetCurrencyValue(result.RootElement, "differenceValueByCurrency", "TRY", "differenceValue"));
        Assert.Equal(150, GetCurrencyValue(result.RootElement, "differenceValueByCurrency", "USD", "differenceValue"));
        var movements = await GetMovementsAsync(atmId, CassetteMovementType.ReconciliationAdjustment);
        Assert.Equal(2, movements.Count);
        Assert.Equal(new[] { 2, 3 }, movements.Select(movement => movement.PhysicalCountDelta).Order().ToArray());

        using var balancesResponse = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var balances = await ParseAsync(balancesResponse);
        Assert.Equal(100, GetCurrencyValue(balances.RootElement, "totalsByCurrency", "TRY", "totalValue"));
        Assert.Equal(170, GetCurrencyValue(balances.RootElement, "totalsByCurrency", "USD", "totalValue"));
    }

    [Fact]
    public async Task Reconciliation_is_rejected_outside_maintenance()
    {
        var atmId = await CreateAtmAsync(inService: true);
        using var response = await PostReconciliationAsync(atmId, Snapshot());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountReconciliationsAsync(atmId));
    }

    [Fact]
    public async Task Reconciliation_rejects_incomplete_cassette_snapshot()
    {
        var atmId = await CreateAtmAsync(inService: true);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);
        var incomplete = new CashReconciliationRequest(Snapshot().Cassettes!.Take(4).ToArray());

        using var response = await PostReconciliationAsync(atmId, incomplete);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountReconciliationsAsync(atmId));
    }

    [Fact]
    public async Task Reconciliation_rejects_counts_over_cassette_capacity()
    {
        var atmId = await CreateAtmAsync(inService: true);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);

        using var response = await PostReconciliationAsync(atmId,
            Snapshot((1, [new(Currency.TRY, BanknoteDenomination.Twenty, 101)])));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountReconciliationsAsync(atmId));
    }

    [Fact]
    public async Task Reconciliation_rejects_a_recycling_cassette_with_the_wrong_note_type()
    {
        var atmId = await CreateAtmAsync(inService: true);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);

        using var response = await PostReconciliationAsync(atmId,
            Snapshot((2, [new(Currency.TRY, BanknoteDenomination.Fifty, 1)])));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountReconciliationsAsync(atmId));
    }

    [Fact]
    public async Task Reconciliation_invalid_snapshot_does_not_partially_change_balances_or_movements()
    {
        var initial = InitialCounts((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)]));
        var atmId = await CreateAtmAsync(inService: true, initialCounts: initial);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);
        var invalid = Snapshot(
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]),
            (5, [new(Currency.EUR, BanknoteDenomination.OneHundred, -1)]));

        using var response = await PostReconciliationAsync(atmId, invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountReconciliationsAsync(atmId));
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.ReconciliationAdjustment));
        using var balancesResponse = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        using var balances = await ParseAsync(balancesResponse);
        Assert.Equal(2, GetCassetteBalanceCount(balances.RootElement, 2, "TRY", "100", "physicalCount"));
    }

    [Fact]
    public async Task Reconciliation_is_blocked_when_a_cassette_has_reserved_notes()
    {
        var initial = InitialCounts((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)]));
        var atmId = await CreateAtmAsync(inService: true, initialCounts: initial);
        await SetAtmStateAsync(atmId, AtmLifecycleState.Maintenance);
        await fixture.SetReservedCountAsync(atmId, cassetteNumber: 2, Currency.TRY, BanknoteDenomination.OneHundred, reservedCount: 1);

        using var response = await PostReconciliationAsync(atmId,
            Snapshot((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)])));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountReconciliationsAsync(atmId));
        Assert.Empty(await GetMovementsAsync(atmId, CassetteMovementType.ReconciliationAdjustment));
    }

    private async Task<Guid> CreateAtmAsync(
        bool inService,
        ConfigureAtmRequest? configuration = null,
        IReadOnlyDictionary<int, BanknoteLoadRequest[]>? initialCounts = null)
    {
        await fixture.ResetDatabaseAsync();
        using var create = await fixture.Client.PostAsJsonAsync(
            "/api/atms",
            new CreateAtmRequest($"PHASE4-{Guid.NewGuid():N}"),
            JsonOptions);
        create.EnsureSuccessStatusCode();
        using var created = await ParseAsync(create);
        var atmId = created.RootElement.GetProperty("id").GetGuid();

        using var configure = await fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/configure",
            configuration ?? Configuration(),
            JsonOptions);
        configure.EnsureSuccessStatusCode();

        var counts = initialCounts ?? InitialCounts();
        var loads = Enumerable.Range(1, 5)
            .Select(number => new CassetteLoadRequest(
                number,
                counts.TryGetValue(number, out var notes) ? notes : Array.Empty<BanknoteLoadRequest>()))
            .ToArray();
        using var load = await fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/load",
            new LoadCashRequest(loads),
            JsonOptions);
        load.EnsureSuccessStatusCode();

        if (inService)
        {
            await SetAtmStateAsync(atmId, AtmLifecycleState.InService);
        }

        return atmId;
    }

    private async Task SetAtmStateAsync(Guid atmId, AtmLifecycleState state)
    {
        using var response = await fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/state",
            new SetAtmStateRequest(state),
            JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> PostDepositAsync(
        Guid atmId,
        IReadOnlyList<BanknoteLoadRequest> notes,
        int unrecognized = 0) =>
        fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/deposits",
            new CashDepositRequest(notes, unrecognized),
            JsonOptions);

    private Task<HttpResponseMessage> PostReconciliationAsync(Guid atmId, CashReconciliationRequest request) =>
        fixture.Client.PostAsJsonAsync($"/api/atms/{atmId}/reconciliations", request, JsonOptions);

    private async Task<CashDeposit> GetDepositAsync(Guid id)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CashDeposits.AsNoTracking().Include(deposit => deposit.Notes).SingleAsync(deposit => deposit.Id == id);
    }

    private async Task<CashReconciliation> GetReconciliationAsync(Guid id)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CashReconciliations.AsNoTracking()
            .Include(reconciliation => reconciliation.Lines)
            .ThenInclude(line => line.Cassette)
            .SingleAsync(reconciliation => reconciliation.Id == id);
    }

    private async Task<List<CassetteMovement>> GetMovementsAsync(Guid atmId, CassetteMovementType type)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CassetteMovements.AsNoTracking()
            .Include(movement => movement.Cassette)
            .Where(movement => movement.AtmId == atmId && movement.Type == type)
            .OrderBy(movement => movement.Cassette.Number)
            .ToListAsync();
    }

    private async Task<int> CountDepositsAsync(Guid atmId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CashDeposits.CountAsync(deposit => deposit.AtmId == atmId);
    }

    private async Task<int> CountReconciliationsAsync(Guid atmId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CashReconciliations.CountAsync(reconciliation => reconciliation.AtmId == atmId);
    }

    private static ConfigureAtmRequest Configuration(
        int depositCapacity = 100,
        int tryHundredCapacity = 100,
        int tryFiftyCapacity = 100,
        int usdTwentyCapacity = 100,
        int slotFiveCapacity = 100,
        bool duplicateTryHundred = false) => new(
    [
        new(1, CassetteType.DepositOnly, Capacity: depositCapacity),
        new(2, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.OneHundred, tryHundredCapacity),
        new(3, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.Fifty, tryFiftyCapacity),
        new(4, CassetteType.Recycling, Currency.USD, BanknoteDenomination.Twenty, usdTwentyCapacity),
        new(5, CassetteType.Recycling,
            duplicateTryHundred ? Currency.TRY : Currency.EUR,
            BanknoteDenomination.OneHundred,
            slotFiveCapacity)
    ]);

    private static Dictionary<int, BanknoteLoadRequest[]> InitialCounts(
        params (int CassetteNumber, BanknoteLoadRequest[] Notes)[] overrides)
    {
        var counts = new Dictionary<int, BanknoteLoadRequest[]>
        {
            [4] = [new(Currency.USD, BanknoteDenomination.Twenty, 1)]
        };
        foreach (var (cassetteNumber, notes) in overrides)
        {
            counts[cassetteNumber] = notes;
        }

        return counts;
    }

    private static CashReconciliationRequest Snapshot(
        params (int CassetteNumber, BanknoteLoadRequest[] Notes)[] overrides)
    {
        var counts = InitialCounts(overrides);
        return new CashReconciliationRequest(Enumerable.Range(1, 5)
            .Select(number => new CassetteCountSnapshotRequest(
                number,
                counts.TryGetValue(number, out var notes) ? notes : Array.Empty<BanknoteLoadRequest>()))
            .ToArray());
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static long GetCurrencyValue(JsonElement root, string arrayName, string currency, string valueProperty) =>
        root.GetProperty(arrayName).EnumerateArray()
            .Single(item => item.GetProperty("currency").GetString() == currency)
            .GetProperty(valueProperty).GetInt64();

    private static int GetCassetteBalanceCount(
        JsonElement root,
        int cassetteNumber,
        string currency,
        string denomination,
        string countProperty) =>
        root.GetProperty("cassettes").EnumerateArray()
            .Single(cassette => cassette.GetProperty("number").GetInt32() == cassetteNumber)
            .GetProperty("balances").EnumerateArray()
            .Single(balance => balance.GetProperty("currency").GetString() == currency &&
                               balance.GetProperty("denomination").GetString() == denomination)
            .GetProperty(countProperty).GetInt32();
}
