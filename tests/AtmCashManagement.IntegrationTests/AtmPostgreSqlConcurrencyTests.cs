using System.Net;
using System.Text.Json;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtmCashManagement.IntegrationTests;

public sealed class AtmPostgreSqlConcurrencyTests(AtmApiFixture fixture) : IClassFixture<AtmApiFixture>
{
    [Fact]
    public async Task Parallel_last_note_quotes_allow_only_one_reservation()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
            },
            Phase6TestSupport.DefaultConfiguration(duplicateTryHundred: true));

        var responses = await Phase6TestSupport.StartTogetherAsync(
            new[] { "4000000000000001", "4000000000000002" },
            card => Phase6TestSupport.PostQuoteAsync(fixture, atmId, card, Currency.TRY, 100));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var balance = await db.CassetteBalances.SingleAsync(row => row.Cassette.AtmId == atmId);
            Assert.Equal(1, balance.PhysicalCount);
            Assert.Equal(1, balance.ReservedCount);
            Assert.Equal(1, await db.CassetteMovements
                .Where(movement => movement.AtmId == atmId && movement.Type == CassetteMovementType.Reservation)
                .SumAsync(movement => movement.ReservedCountDelta));
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
    public async Task Parallel_quotes_never_reserve_more_than_the_atm_stock()
    {
        const int stock = 5;
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, stock)]
            },
            Phase6TestSupport.DefaultConfiguration(duplicateTryHundred: true));

        var cards = Enumerable.Range(1, 12).Select(number => $"400000000000{number:D4}").ToArray();
        var responses = await Phase6TestSupport.StartTogetherAsync(
            cards,
            card => Phase6TestSupport.PostQuoteAsync(fixture, atmId, card, Currency.TRY, 100));
        try
        {
            Assert.Equal(stock, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(cards.Length - stock,
                responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var balance = await db.CassetteBalances.SingleAsync(row => row.Cassette.AtmId == atmId);
            var ledgerReservation = await db.CassetteMovements
                .Where(movement => movement.AtmId == atmId && movement.Type == CassetteMovementType.Reservation)
                .SumAsync(movement => movement.ReservedCountDelta);
            Assert.Equal(stock, balance.PhysicalCount);
            Assert.Equal(stock, balance.ReservedCount);
            Assert.Equal(balance.ReservedCount, ledgerReservation);
            Assert.True(balance.ReservedCount <= balance.PhysicalCount);
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
    public async Task Parallel_confirm_with_same_key_debits_once_and_replays_same_result()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
            });
        using var quoteResponse = await Phase6TestSupport.PostQuoteAsync(
            fixture, atmId, "4000000000000010", Currency.TRY, 100);
        quoteResponse.EnsureSuccessStatusCode();
        using var quote = await Phase6TestSupport.ReadAsync(quoteResponse);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();

        var responses = await Phase6TestSupport.StartTogetherAsync(
            new[] { 1, 2 },
            _ => Phase6TestSupport.PostConfirmAsync(fixture, quoteId, optionId, "phase6-parallel-confirm"));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var results = await Task.WhenAll(responses.Select(Phase6TestSupport.ReadAsync));
            try
            {
                Assert.Equal(
                    results[0].RootElement.GetProperty("transactionId").GetGuid(),
                    results[1].RootElement.GetProperty("transactionId").GetGuid());
            }
            finally
            {
                foreach (var result in results)
                {
                    result.Dispose();
                }
            }

            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            Assert.Equal(1, await db.CashWithdrawals.CountAsync(withdrawal => withdrawal.QuoteId == quoteId));
            var balance = await db.CassetteBalances.SingleAsync(row => row.Cassette.AtmId == atmId);
            Assert.Equal(0, balance.PhysicalCount);
            Assert.Equal(0, balance.ReservedCount);
            Assert.Equal(-1, await db.CassetteMovements
                .Where(movement => movement.AtmId == atmId && movement.Type == CassetteMovementType.Withdrawal)
                .SumAsync(movement => movement.PhysicalCountDelta));
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
    public async Task Parallel_confirm_and_cancel_have_one_winner_and_consistent_ledger()
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
            });
        using var quoteResponse = await Phase6TestSupport.PostQuoteAsync(
            fixture, atmId, "4000000000000020", Currency.TRY, 100);
        quoteResponse.EnsureSuccessStatusCode();
        using var quote = await Phase6TestSupport.ReadAsync(quoteResponse);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();

        var responses = await Phase6TestSupport.StartTogetherAsync(
            new[] { "confirm", "cancel" },
            action => action == "confirm"
                ? Phase6TestSupport.PostConfirmAsync(fixture, quoteId, optionId, "phase6-confirm-cancel-race")
                : Phase6TestSupport.PostCancelAsync(fixture, quoteId));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

            using var consistency = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId);
            Assert.True(consistency.RootElement.GetProperty("isConsistent").GetBoolean());
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var storedQuote = await db.WithdrawalQuotes.SingleAsync(row => row.Id == quoteId);
            var withdrawals = await db.CashWithdrawals.CountAsync(row => row.QuoteId == quoteId);
            Assert.Equal(storedQuote.Status == WithdrawalQuoteStatus.Confirmed ? 1 : 0, withdrawals);
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
    public async Task Same_card_parallel_quotes_on_two_atms_share_one_daily_limit_row()
    {
        var configuration = new ConfigureAtmRequest(
        [
            new CassetteDefinitionRequest(1, CassetteType.DepositOnly),
            new CassetteDefinitionRequest(2, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.FiveHundred),
            new CassetteDefinitionRequest(3, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.FiveHundred),
            new CassetteDefinitionRequest(4, CassetteType.Recycling, Currency.USD, BanknoteDenomination.Twenty),
            new CassetteDefinitionRequest(5, CassetteType.Recycling, Currency.EUR, BanknoteDenomination.OneHundred)
        ]);
        var cash = new Dictionary<int, BanknoteLoadRequest[]>
        {
            [2] = [new(Currency.TRY, BanknoteDenomination.FiveHundred, 100)],
            [3] = [new(Currency.TRY, BanknoteDenomination.FiveHundred, 100)]
        };

        var firstAtmId = await Phase6TestSupport.CreateAtmAsync(fixture, cash, configuration);
        var secondAtmId = await Phase6TestSupport.CreateAtmAsync(fixture, cash, configuration, resetDatabase: false);
        const string cardNumber = "4000000000000030";
        var responses = await Phase6TestSupport.StartTogetherAsync(
            new[] { firstAtmId, secondAtmId },
            atmId => Phase6TestSupport.PostQuoteAsync(fixture, atmId, cardNumber, Currency.TRY, 20_000));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var usage = await db.CardDailyLimitUsages
                .Where(row => row.Currency == Currency.TRY)
                .SingleAsync();
            Assert.Equal(20_000, usage.ConfirmedAmount + usage.ReservedAmount);
            Assert.True(usage.ConfirmedAmount + usage.ReservedAmount <= 20_000);
            var activeQuoteCount = await db.WithdrawalQuotes
                .CountAsync(row => row.Status == WithdrawalQuoteStatus.Active);
            Assert.Equal(1, activeQuoteCount);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
