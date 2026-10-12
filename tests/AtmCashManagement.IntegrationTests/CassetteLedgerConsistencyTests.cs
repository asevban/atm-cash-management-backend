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

namespace AtmCashManagement.IntegrationTests;

public sealed class CassetteLedgerConsistencyTests(AtmApiFixture fixture) : IClassFixture<AtmApiFixture>
{
    [Fact]
    public async Task Confirming_lower_option_records_withdrawal_and_release_for_same_cassette_and_denomination()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)]
            });

        Guid quoteId;
        Guid lowerOptionId;
        using (var quoteResponse = await Phase6TestSupport.PostQuoteAsync(
                   fixture, atmId, "4000000000000199", Currency.TRY, 150))
        {
            quoteResponse.EnsureSuccessStatusCode();
            using var quote = await Phase6TestSupport.ReadAsync(quoteResponse);
            var options = quote.RootElement.GetProperty("options").EnumerateArray().ToArray();
            var lower = Assert.Single(options, option => option.GetProperty("kind").GetString() == "LOWER");
            var upper = Assert.Single(options, option => option.GetProperty("kind").GetString() == "UPPER");
            Assert.Equal(100, lower.GetProperty("amount").GetInt32());
            Assert.Equal(200, upper.GetProperty("amount").GetInt32());
            quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
            lowerOptionId = lower.GetProperty("optionId").GetGuid();
        }

        using (var confirm = await Phase6TestSupport.PostConfirmAsync(
                   fixture, quoteId, lowerOptionId, "phase6-same-denomination-withdrawal-release"))
        {
            confirm.EnsureSuccessStatusCode();
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var cassetteId = await db.Cassettes
                .Where(cassette => cassette.AtmId == atmId && cassette.Number == 2)
                .Select(cassette => cassette.Id)
                .SingleAsync();
            var movements = await db.CassetteMovements
                .Where(movement => movement.AtmId == atmId &&
                                   movement.CassetteId == cassetteId &&
                                   movement.Currency == Currency.TRY &&
                                   movement.Denomination == BanknoteDenomination.OneHundred &&
                                   movement.Type != CassetteMovementType.Load)
                .OrderBy(movement => movement.Type)
                .ToListAsync();

            var withdrawal = Assert.Single(movements, movement => movement.Type == CassetteMovementType.Withdrawal);
            Assert.Equal(-1, withdrawal.PhysicalCountDelta);
            Assert.Equal(-1, withdrawal.ReservedCountDelta);
            var release = Assert.Single(movements, movement => movement.Type == CassetteMovementType.ReservationRelease);
            Assert.Equal(0, release.PhysicalCountDelta);
            Assert.Equal(-1, release.ReservedCountDelta);
            Assert.Equal(withdrawal.TransactionReference, release.TransactionReference);

            var balance = await db.CassetteBalances.SingleAsync(item =>
                item.CassetteId == cassetteId && item.Currency == Currency.TRY &&
                item.Denomination == BanknoteDenomination.OneHundred);
            Assert.Equal(1, balance.PhysicalCount);
            Assert.Equal(0, balance.ReservedCount);
        }

        using var consistency = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId);
        AssertConsistent(consistency.RootElement);
    }

    [Fact]
    public async Task Ledger_matches_balance_after_load_deposit_reserve_release_confirm_expire_and_reconcile()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [1] =
                [
                    new(Currency.TRY, BanknoteDenomination.Fifty, 2),
                    new(Currency.EUR, BanknoteDenomination.OneHundred, 1)
                ],
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)],
                [3] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)],
                [4] = [new(Currency.USD, BanknoteDenomination.Twenty, 1)],
                [5] = [new(Currency.EUR, BanknoteDenomination.OneHundred, 1)]
            },
            Phase6TestSupport.DefaultConfiguration(duplicateTryHundred: true));

        using (var afterLoad = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId))
        {
            AssertConsistent(afterLoad.RootElement);
            Assert.Equal(5, afterLoad.RootElement.GetProperty("checkedCassetteCount").GetInt32());
            var depositOnly = afterLoad.RootElement.GetProperty("cassettes").EnumerateArray()
                .Single(cassette => cassette.GetProperty("cassetteNumber").GetInt32() == 1);
            Assert.Equal(2, depositOnly.GetProperty("balances").GetArrayLength());
        }

        using (var deposit = await fixture.Client.PostAsJsonAsync(
                   $"/api/atms/{atmId}/deposits",
                   new CashDepositRequest(
                   [
                       new(Currency.TRY, BanknoteDenomination.Fifty, 1),
                       new(Currency.EUR, BanknoteDenomination.Twenty, 2)
                   ],
                   UnrecognizedNoteCount: 0),
                   Phase6TestSupport.JsonOptions))
        {
            deposit.EnsureSuccessStatusCode();
        }
        using (var afterDeposit = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId))
        {
            AssertConsistent(afterDeposit.RootElement);
            var depositOnly = afterDeposit.RootElement.GetProperty("cassettes").EnumerateArray()
                .Single(cassette => cassette.GetProperty("cassetteNumber").GetInt32() == 1);
            Assert.Equal(3, depositOnly.GetProperty("balances").GetArrayLength());
        }

        using (var quoteResponse = await Phase6TestSupport.PostQuoteAsync(
                   fixture, atmId, "4000000000000100", Currency.TRY, 100))
        {
            quoteResponse.EnsureSuccessStatusCode();
            using var quote = await Phase6TestSupport.ReadAsync(quoteResponse);
            var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
            var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
            using (var afterReserve = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId))
            {
                AssertConsistent(afterReserve.RootElement);
                var cassetteTwoBalance = afterReserve.RootElement.GetProperty("cassettes").EnumerateArray()
                    .Single(cassette => cassette.GetProperty("cassetteNumber").GetInt32() == 2)
                    .GetProperty("balances").EnumerateArray()
                    .Single(balance => balance.GetProperty("currency").GetString() == "TRY" &&
                                       balance.GetProperty("denomination").GetString() == "100");
                var cassetteThreeBalance = afterReserve.RootElement.GetProperty("cassettes").EnumerateArray()
                    .Single(cassette => cassette.GetProperty("cassetteNumber").GetInt32() == 3)
                    .GetProperty("balances").EnumerateArray()
                    .Single(balance => balance.GetProperty("currency").GetString() == "TRY" &&
                                       balance.GetProperty("denomination").GetString() == "100");
                Assert.Equal(1, cassetteTwoBalance.GetProperty("databaseReservedCount").GetInt64());
                Assert.Equal(1, cassetteTwoBalance.GetProperty("ledgerReservedCount").GetInt64());
                Assert.Equal(0, cassetteThreeBalance.GetProperty("databaseReservedCount").GetInt64());
                Assert.Equal(0, cassetteThreeBalance.GetProperty("ledgerReservedCount").GetInt64());
            }

            using var cancel = await Phase6TestSupport.PostCancelAsync(fixture, quoteId);
            cancel.EnsureSuccessStatusCode();
            using var afterCancel = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId);
            AssertConsistent(afterCancel.RootElement);

            using var confirmedQuoteResponse = await Phase6TestSupport.PostQuoteAsync(
                fixture, atmId, "4000000000000100", Currency.TRY, 100);
            confirmedQuoteResponse.EnsureSuccessStatusCode();
            using var confirmedQuote = await Phase6TestSupport.ReadAsync(confirmedQuoteResponse);
            var confirmedQuoteId = confirmedQuote.RootElement.GetProperty("quoteId").GetGuid();
            var confirmedOptionId = confirmedQuote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
            using var confirm = await Phase6TestSupport.PostConfirmAsync(
                fixture, confirmedQuoteId, confirmedOptionId, "phase6-ledger-confirm");
            confirm.EnsureSuccessStatusCode();
            using var afterConfirm = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId);
            AssertConsistent(afterConfirm.RootElement);
            Assert.Contains(afterConfirm.RootElement.GetProperty("cassettes").EnumerateArray()
                .SelectMany(cassette => cassette.GetProperty("balances").EnumerateArray()),
                balance => balance.GetProperty("databasePhysicalCount").GetInt64() == 1 &&
                           balance.GetProperty("ledgerPhysicalCount").GetInt64() == 1 &&
                           balance.GetProperty("databaseReservedCount").GetInt64() == 0 &&
                           balance.GetProperty("ledgerReservedCount").GetInt64() == 0);
        }

        using (var expiringQuoteResponse = await Phase6TestSupport.PostQuoteAsync(
                   fixture, atmId, "4000000000000101", Currency.TRY, 100))
        {
            expiringQuoteResponse.EnsureSuccessStatusCode();
            using var expiringQuote = await Phase6TestSupport.ReadAsync(expiringQuoteResponse);
            var quoteId = expiringQuote.RootElement.GetProperty("quoteId").GetGuid();
            fixture.Clock.Advance(TimeSpan.FromSeconds(61));
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var quotes = scope.ServiceProvider.GetRequiredService<WithdrawalQuoteService>();
                await quotes.ExpireDueQuotesAsync(CancellationToken.None);
            }
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
                var expiredQuote = await db.WithdrawalQuotes.SingleAsync(quote => quote.Id == quoteId);
                Assert.Equal(WithdrawalQuoteStatus.Expired, expiredQuote.Status);
            }
            using var afterExpire = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId);
            AssertConsistent(afterExpire.RootElement);
        }

        using (var maintenance = await fixture.Client.PostAsJsonAsync(
                   $"/api/atms/{atmId}/state",
                   new SetAtmStateRequest(AtmLifecycleState.Maintenance),
                   Phase6TestSupport.JsonOptions))
        {
            maintenance.EnsureSuccessStatusCode();
        }
        var reconciliation = await BuildReconciliationSnapshotAsync(atmId, increaseCassetteTwoByOne: true);
        using (var reconcile = await Phase6TestSupport.PostReconciliationAsync(fixture, atmId, reconciliation))
        {
            reconcile.EnsureSuccessStatusCode();
        }
        using var afterReconciliation = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId);
        AssertConsistent(afterReconciliation.RootElement);
    }

    [Fact]
    public async Task Consistency_endpoint_reports_database_only_and_ledger_only_mismatches_without_repairing_them()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
            });

        using (var initial = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId))
        {
            AssertConsistent(initial.RootElement);
            Assert.Equal(5, initial.RootElement.GetProperty("cassettes").GetArrayLength());
            foreach (var emptyCassetteNumber in new[] { 3, 4, 5 })
            {
                var emptyCassette = initial.RootElement.GetProperty("cassettes").EnumerateArray()
                    .Single(cassette => cassette.GetProperty("cassetteNumber").GetInt32() == emptyCassetteNumber);
                Assert.Empty(emptyCassette.GetProperty("balances").EnumerateArray());
            }
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var cassetteId = await db.Cassettes.Where(cassette => cassette.AtmId == atmId && cassette.Number == 2)
                .Select(cassette => cassette.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE cassette_balances SET \"PhysicalCount\" = \"PhysicalCount\" + 1 WHERE \"CassetteId\" = {cassetteId}");

            var depositOnlyCassetteId = await db.Cassettes
                .Where(cassette => cassette.AtmId == atmId && cassette.Number == 1)
                .Select(cassette => cassette.Id)
                .SingleAsync();
            db.CassetteMovements.Add(new CassetteMovement(
                atmId,
                depositOnlyCassetteId,
                CassetteMovementType.Load,
                Currency.TRY,
                BanknoteDenomination.Twenty,
                physicalCountDelta: 1,
                reservedCountDelta: 0,
                DateTimeOffset.UtcNow,
                "phase6-ledger-only-test-row"));
            await db.SaveChangesAsync();
        }

        using (var mismatch = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId))
        {
            var root = mismatch.RootElement;
            Assert.False(root.GetProperty("isConsistent").GetBoolean());
            Assert.Equal(2, root.GetProperty("inconsistentBalanceCount").GetInt32());
            var entry = root.GetProperty("cassettes").EnumerateArray()
                .Single(cassette => cassette.GetProperty("cassetteNumber").GetInt32() == 2)
                .GetProperty("balances")[0];
            Assert.Equal(2, entry.GetProperty("databasePhysicalCount").GetInt64());
            Assert.Equal(1, entry.GetProperty("ledgerPhysicalCount").GetInt64());
            Assert.Equal(1, entry.GetProperty("physicalDifference").GetInt64());
            Assert.False(entry.GetProperty("isConsistent").GetBoolean());

            var ledgerOnly = root.GetProperty("cassettes").EnumerateArray()
                .Single(cassette => cassette.GetProperty("cassetteNumber").GetInt32() == 1)
                .GetProperty("balances").EnumerateArray()
                .Single(balance => balance.GetProperty("denomination").GetString() == "20");
            Assert.Equal(0, ledgerOnly.GetProperty("databasePhysicalCount").GetInt64());
            Assert.Equal(1, ledgerOnly.GetProperty("ledgerPhysicalCount").GetInt64());
            Assert.Equal(-1, ledgerOnly.GetProperty("physicalDifference").GetInt64());
        }

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AtmDbContext>();
        Assert.Equal(2, await verificationDb.CassetteBalances
            .Where(balance => balance.Cassette.AtmId == atmId)
            .Select(balance => balance.PhysicalCount)
            .SingleAsync());
    }

    [Fact]
    public async Task Consistency_endpoint_returns_problem_details_for_unknown_atm()
    {
        using var response = await fixture.Client.GetAsync($"/api/atms/{Guid.NewGuid()}/ledger/consistency");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Movement_rows_cannot_be_updated_deleted_or_duplicated_by_the_same_event_key()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
            });
        CassetteMovement original;
        string originalTransactionReference;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            original = await db.CassetteMovements
                .SingleAsync(movement => movement.AtmId == atmId && movement.Type == CassetteMovementType.Load);
            originalTransactionReference = original.TransactionReference;
            db.Entry(original).Property(movement => movement.TransactionReference).CurrentValue = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var movement = await db.CassetteMovements
                .SingleAsync(item => item.AtmId == atmId && item.Type == CassetteMovementType.Load);
            db.CassetteMovements.Remove(movement);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var cassetteId = await db.Cassettes.Where(cassette => cassette.AtmId == atmId && cassette.Number == 2)
                .Select(cassette => cassette.Id).SingleAsync();
            db.CassetteMovements.Add(new CassetteMovement(
                atmId,
                cassetteId,
                CassetteMovementType.Load,
                Currency.TRY,
                BanknoteDenomination.OneHundred,
                physicalCountDelta: 1,
                reservedCountDelta: 0,
                DateTimeOffset.UtcNow,
                originalTransactionReference));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var verifyScope = fixture.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AtmDbContext>();
        Assert.Equal(1, await verifyDb.CassetteMovements.CountAsync(movement => movement.AtmId == atmId));
    }

    private async Task<CashReconciliationRequest> BuildReconciliationSnapshotAsync(Guid atmId, bool increaseCassetteTwoByOne)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        var cassettes = await db.Cassettes.AsNoTracking()
            .Where(cassette => cassette.AtmId == atmId)
            .Include(cassette => cassette.Balances)
            .OrderBy(cassette => cassette.Number)
            .ToListAsync();
        return new CashReconciliationRequest(cassettes.Select(cassette =>
        {
            var notes = cassette.Balances
                .Select(balance => new BanknoteLoadRequest(
                    balance.Currency,
                    balance.Denomination,
                    balance.PhysicalCount + (increaseCassetteTwoByOne && cassette.Number == 2 && balance.Denomination == BanknoteDenomination.OneHundred ? 1 : 0)))
                .ToArray();
            return new CassetteCountSnapshotRequest(cassette.Number, notes);
        }).ToArray());
    }

    private static void AssertConsistent(JsonElement root)
    {
        Assert.True(root.GetProperty("isConsistent").GetBoolean(),
            string.Join(Environment.NewLine, root.GetProperty("inconsistencies").EnumerateArray().Select(item => item.GetString())));
        Assert.Equal(0, root.GetProperty("inconsistentBalanceCount").GetInt32());
        foreach (var balance in root.GetProperty("cassettes").EnumerateArray()
                     .SelectMany(cassette => cassette.GetProperty("balances").EnumerateArray()))
        {
            Assert.Equal(0, balance.GetProperty("physicalDifference").GetInt64());
            Assert.Equal(0, balance.GetProperty("reservedDifference").GetInt64());
            Assert.Equal(0, balance.GetProperty("availableDifference").GetInt64());
            Assert.True(balance.GetProperty("isConsistent").GetBoolean());
        }
    }
}
