using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AtmCashManagement.IntegrationTests;

public sealed class OperationsReportingAndOutboxTests(AtmApiFixture fixture) : IClassFixture<AtmApiFixture>
{
    [Fact]
    public async Task Operations_report_summarizes_mixed_cassettes_and_network_in_try_without_changing_balances()
    {
        var firstNotes = new Dictionary<int, BanknoteLoadRequest[]>
        {
            [1] = [new(Currency.TRY, BanknoteDenomination.Fifty, 2), new(Currency.USD, BanknoteDenomination.Twenty, 1), new(Currency.EUR, BanknoteDenomination.OneHundred, 1)],
            [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 10)],
            [3] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)],
            [4] = [new(Currency.USD, BanknoteDenomination.Twenty, 1)],
            [5] = [new(Currency.EUR, BanknoteDenomination.OneHundred, 2)]
        };
        var firstAtm = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            firstNotes,
            Phase6TestSupport.DefaultConfiguration(duplicateTryHundred: true));
        var secondAtm = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 5)]
            },
            resetDatabase: false);

        int movementCountBefore;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            movementCountBefore = await scope.ServiceProvider.GetRequiredService<AtmDbContext>()
                .CassetteMovements.CountAsync();
        }

        using var filteredResponse = await fixture.Client.GetAsync($"/api/reports/operations?atmId={firstAtm}");
        filteredResponse.EnsureSuccessStatusCode();
        using var filtered = await JsonDocument.ParseAsync(await filteredResponse.Content.ReadAsStreamAsync());
        var firstReport = filtered.RootElement.GetProperty("atms").EnumerateArray().Single();
        var cassettes = firstReport.GetProperty("cassettes").EnumerateArray().ToArray();
        Assert.Equal(5, cassettes.Length);
        var depositCassette = cassettes.Single(cassette => cassette.GetProperty("number").GetInt32() == 1);
        Assert.Equal(4, depositCassette.GetProperty("totalBanknoteCount").GetInt32());
        Assert.Equal(3, depositCassette.GetProperty("balances").GetArrayLength());
        Assert.All(depositCassette.GetProperty("balances").EnumerateArray(), balance =>
            Assert.False(balance.GetProperty("withdrawalEligible").GetBoolean()));
        Assert.Equal(14100m, firstReport.GetProperty("physicalValueTry").GetDecimal());

        var tryTotals = firstReport.GetProperty("totalsByCurrency").EnumerateArray()
            .Single(total => total.GetProperty("currency").GetString() == "TRY");
        Assert.Equal(1300m, tryTotals.GetProperty("physicalValue").GetDecimal());
        Assert.Equal(1300m, tryTotals.GetProperty("availableValue").GetDecimal());
        Assert.Equal(1200m, tryTotals.GetProperty("withdrawableAvailableValue").GetDecimal());
        var usdTotals = firstReport.GetProperty("totalsByCurrency").EnumerateArray()
            .Single(total => total.GetProperty("currency").GetString() == "USD");
        Assert.Equal(40m, usdTotals.GetProperty("physicalValue").GetDecimal());
        Assert.Equal(35m, usdTotals.GetProperty("rateToTry").GetDecimal());

        using var networkResponse = await fixture.Client.GetAsync("/api/reports/operations");
        networkResponse.EnsureSuccessStatusCode();
        using var network = await JsonDocument.ParseAsync(await networkResponse.Content.ReadAsStreamAsync());
        Assert.Equal(2, network.RootElement.GetProperty("atms").GetArrayLength());
        Assert.Equal(14600m, network.RootElement.GetProperty("networkPhysicalValueTry").GetDecimal());

        await using var verifyScope = fixture.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AtmDbContext>();
        Assert.Equal(movementCountBefore, await db.CassetteMovements.CountAsync());
        Assert.Empty(await db.OutboxEvents.ToListAsync());
        Assert.NotEqual(firstAtm, secondAtm);
    }

    [Fact]
    public async Task Reservation_threshold_events_are_atomic_retryable_and_not_duplicated_by_withdrawal()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 12)]
            });

        using (var quoteResponse = await Phase6TestSupport.PostQuoteAsync(fixture, atmId, "4111111111111111", Currency.TRY, 300))
        {
            quoteResponse.EnsureSuccessStatusCode();
            using var quote = await Phase6TestSupport.ReadAsync(quoteResponse);
            var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
            var optionId = quote.RootElement.GetProperty("options").EnumerateArray().Single().GetProperty("optionId").GetGuid();

            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
                var row = await db.OutboxEvents.SingleAsync();
                Assert.Equal("LOW_CASH", row.EventType);
                Assert.Equal("Pending", row.Status.ToString());
                using var payload = JsonDocument.Parse(row.Payload);
                Assert.Equal(12, payload.RootElement.GetProperty("previousAvailable").GetInt32());
                Assert.Equal(9, payload.RootElement.GetProperty("currentAvailable").GetInt32());
            }

            using var confirm = await Phase6TestSupport.PostConfirmAsync(fixture, quoteId, optionId, $"outbox-confirm-{Guid.NewGuid():N}");
            confirm.EnsureSuccessStatusCode();

            using (var deposit = await fixture.Client.PostAsJsonAsync(
                       $"/api/atms/{atmId}/deposits",
                       new CashDepositRequest([new(Currency.TRY, BanknoteDenomination.OneHundred, 1)], 0),
                       Phase6TestSupport.JsonOptions))
            {
                deposit.EnsureSuccessStatusCode();
            }

            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
                Assert.Equal(1, await db.OutboxEvents.CountAsync());
                Assert.Equal(10, (await db.CassetteBalances.SingleAsync(item =>
                    item.Cassette.AtmId == atmId && item.Cassette.Number == 2)).AvailableCount);
            }

            using var secondQuoteResponse = await Phase6TestSupport.PostQuoteAsync(fixture, atmId, "4111111111111111", Currency.TRY, 100);
            secondQuoteResponse.EnsureSuccessStatusCode();
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AtmDbContext>().OutboxEvents.CountAsync());
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AtmDbContext>();
            var clock = services.GetRequiredService<IClock>();
            var failing = new OutboxEventProcessor(
                db,
                new FailingPublisher(),
                clock,
                services.GetRequiredService<ILogger<OutboxEventProcessor>>());
            Assert.Equal(0, await failing.ProcessPendingBatchAsync(CancellationToken.None));
            var rows = await db.OutboxEvents.OrderBy(item => item.CreatedAtUtc).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, item =>
            {
                Assert.Equal(OutboxEventStatus.Pending, item.Status);
                Assert.Equal(1, item.RetryCount);
                Assert.Null(item.SentAtUtc);
            });

            var blockingPublisher = new BlockingPublisher();
            var firstWorker = new OutboxEventProcessor(
                db,
                blockingPublisher,
                clock,
                services.GetRequiredService<ILogger<OutboxEventProcessor>>());
            var firstWorkerTask = firstWorker.ProcessPendingBatchAsync(CancellationToken.None);
            await blockingPublisher.WaitUntilEnteredAsync();

            await using var secondWorkerScope = fixture.Services.CreateAsyncScope();
            var secondServices = secondWorkerScope.ServiceProvider;
            var secondWorker = new OutboxEventProcessor(
                secondServices.GetRequiredService<AtmDbContext>(),
                secondServices.GetRequiredService<IOutboxEventPublisher>(),
                clock,
                secondServices.GetRequiredService<ILogger<OutboxEventProcessor>>());
            Assert.Equal(0, await secondWorker.ProcessPendingBatchAsync(CancellationToken.None));
            blockingPublisher.Release();
            Assert.Equal(2, await firstWorkerTask);
            Assert.All(await db.OutboxEvents.ToListAsync(), item =>
            {
                Assert.Equal(OutboxEventStatus.Sent, item.Status);
                Assert.NotNull(item.SentAtUtc);
                Assert.Equal(1, item.RetryCount);
            });
            var balance = await db.CassetteBalances.SingleAsync(item => item.Cassette.AtmId == atmId && item.Cassette.Number == 2);
            Assert.Equal(10, balance.PhysicalCount);
            Assert.Equal(1, balance.ReservedCount);
            Assert.Equal(9, balance.AvailableCount);
        }
    }

    [Fact]
    public async Task Outbox_insert_failure_rolls_back_the_quote_reservation_and_ledger_movement()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 12)]
            });
        await fixture.SetOutboxInsertFailureTriggerAsync(true);
        try
        {
            using var response = await Phase6TestSupport.PostQuoteAsync(fixture, atmId, "4111111111111111", Currency.TRY, 300);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var balance = await db.CassetteBalances.SingleAsync(item => item.Cassette.AtmId == atmId && item.Cassette.Number == 2);
            Assert.Equal(12, balance.PhysicalCount);
            Assert.Equal(0, balance.ReservedCount);
            Assert.Equal(0, await db.WithdrawalQuotes.CountAsync());
            Assert.Equal(0, await db.CassetteMovements.CountAsync(item => item.Type == CassetteMovementType.Reservation));
            Assert.Empty(await db.OutboxEvents.ToListAsync());
        }
        finally
        {
            await fixture.SetOutboxInsertFailureTriggerAsync(false);
        }
    }

    [Fact]
    public async Task Deposit_only_inventory_reconciliation_does_not_create_a_low_cash_event()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [1] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 12)]
            });
        using (var maintenance = await fixture.Client.PostAsJsonAsync(
                   $"/api/atms/{atmId}/state",
                   new SetAtmStateRequest(AtmLifecycleState.Maintenance),
                   Phase6TestSupport.JsonOptions))
        {
            maintenance.EnsureSuccessStatusCode();
        }

        var snapshot = Enumerable.Range(1, 5)
            .Select(number => new CassetteCountSnapshotRequest(number, Array.Empty<BanknoteLoadRequest>()))
            .ToArray();
        using var reconciliation = await Phase6TestSupport.PostReconciliationAsync(
            fixture,
            atmId,
            new CashReconciliationRequest(snapshot));
        reconciliation.EnsureSuccessStatusCode();

        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AtmDbContext>().OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task Operations_report_rejects_a_missing_exchange_rate_instead_of_returning_partial_totals()
    {
        await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
            });
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            db.ExchangeRates.Remove(await db.ExchangeRates.SingleAsync(item => item.BaseCurrency == Currency.USD));
            await db.SaveChangesAsync();
        }

        using var response = await fixture.Client.GetAsync("/api/reports/operations");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "operations_report_exchange_rate_unavailable",
            problem.RootElement.GetProperty("errorCode").GetString());
    }

    private sealed class FailingPublisher : IOutboxEventPublisher
    {
        public Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Intentional test publisher failure.");
    }

    private sealed class BlockingPublisher : IOutboxEventPublisher
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _entered.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }
        }

        public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => _released.TrySetResult();
    }
}
