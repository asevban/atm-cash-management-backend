using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtmCashManagement.IntegrationTests;

public sealed class AtmWithdrawalTests(AtmApiFixture fixture) : IClassFixture<AtmApiFixture>
{
    private const string CardOne = "4111111111111111";
    private const string CardTwo = "5555555555554444";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public async Task Exact_quote_reserves_recycling_notes_without_reducing_physical_stock()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)])));

        using var response = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 200);
        using var result = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ACTIVE", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(200, result.RootElement.GetProperty("requestedAmount").GetInt32());
        Assert.InRange((result.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset() - fixture.Clock.UtcNow).TotalSeconds, 1, 60);
        var option = Assert.Single(result.RootElement.GetProperty("options").EnumerateArray());
        Assert.Equal("EXACT", option.GetProperty("kind").GetString());
        Assert.Equal(2, option.GetProperty("noteCount").GetInt32());
        Assert.Equal(2, option.GetProperty("allocations")[0].GetProperty("count").GetInt32());

        using var balance = await GetBalancesAsync(atmId);
        AssertCassetteBalance(balance.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 2, reserved: 2, available: 0);
        var movements = await GetMovementsAsync(atmId);
        var reservation = Assert.Single(movements, movement => movement.Type == CassetteMovementType.Reservation);
        Assert.Equal(0, reservation.PhysicalCountDelta);
        Assert.Equal(2, reservation.ReservedCountDelta);
    }

    [Fact]
    public async Task Lower_and_upper_options_share_the_maximum_cassette_reservation()
    {
        var atmId = await CreateAtmAsync(InitialNotes(
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]),
            (3, [new(Currency.TRY, BanknoteDenomination.Twenty, 2)])),
            Configuration(slot3Denomination: BanknoteDenomination.Twenty));

        using var response = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 130);
        using var result = await ParseAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = result.RootElement.GetProperty("options").EnumerateArray().ToArray();
        Assert.Equal(2, options.Length);
        var lower = Assert.Single(options, option => option.GetProperty("kind").GetString() == "LOWER");
        var upper = Assert.Single(options, option => option.GetProperty("kind").GetString() == "UPPER");
        Assert.Equal(120, lower.GetProperty("amount").GetInt32());
        Assert.Equal(140, upper.GetProperty("amount").GetInt32());
        Assert.Equal(1, lower.GetProperty("allocations").EnumerateArray().Single(item => item.GetProperty("cassetteNumber").GetInt32() == 2).GetProperty("count").GetInt32());
        Assert.Equal(1, lower.GetProperty("allocations").EnumerateArray().Single(item => item.GetProperty("cassetteNumber").GetInt32() == 3).GetProperty("count").GetInt32());
        Assert.Equal(2, upper.GetProperty("allocations").EnumerateArray().Single(item => item.GetProperty("cassetteNumber").GetInt32() == 3).GetProperty("count").GetInt32());

        var quoteId = result.RootElement.GetProperty("quoteId").GetGuid();
        var reservationRows = await GetQuoteReservationsAsync(quoteId);
        Assert.Equal(new[] { (2, 1), (3, 2) }, reservationRows.Select(row => (row.Cassette.Number, row.ReservedCount)).ToArray());
        Assert.Equal(3, reservationRows.Sum(row => row.ReservedCount));
        var usage = await GetDailyUsageAsync(CardOne, Currency.TRY);
        Assert.Equal(140, usage.ReservedAmount);
    }

    [Fact]
    public async Task Confirming_lower_option_consumes_selected_notes_and_releases_surplus_reservation()
    {
        var atmId = await CreateAtmAsync(InitialNotes(
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]),
            (3, [new(Currency.TRY, BanknoteDenomination.Twenty, 2)])),
            Configuration(slot3Denomination: BanknoteDenomination.Twenty));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 130);
        var options = quote.RootElement.GetProperty("options").EnumerateArray().ToArray();
        var lower = options.Single(option => option.GetProperty("kind").GetString() == "LOWER");
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = lower.GetProperty("optionId").GetGuid();
        quote.Dispose();

        using var response = await PostConfirmAsync(quoteId, optionId, "withdraw-lower-1");
        using var result = await ParseAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(120, result.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal(2, result.RootElement.GetProperty("allocations").EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()));

        using var balances = await GetBalancesAsync(atmId);
        AssertCassetteBalance(balances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 0, reserved: 0, available: 0);
        AssertCassetteBalance(balances.RootElement, 3, Currency.TRY, BanknoteDenomination.Twenty, physical: 1, reserved: 0, available: 1);
        var movements = await GetMovementsAsync(atmId);
        Assert.Equal(2, movements.Count(movement => movement.Type == CassetteMovementType.Withdrawal));
        Assert.Equal(new[] { -1, -1 }, movements.Where(movement => movement.Type == CassetteMovementType.Withdrawal)
            .Select(movement => movement.PhysicalCountDelta).Order().ToArray());
        var release = Assert.Single(movements, movement => movement.Type == CassetteMovementType.ReservationRelease);
        Assert.Equal(0, release.PhysicalCountDelta);
        Assert.Equal(-1, release.ReservedCountDelta);
        var usage = await GetDailyUsageAsync(CardOne, Currency.TRY);
        Assert.Equal(120, usage.ConfirmedAmount);
        Assert.Equal(0, usage.ReservedAmount);
    }

    [Fact]
    public async Task Deposit_only_cash_is_not_offered_for_withdrawal()
    {
        var atmId = await CreateAtmAsync(InitialNotes(
            (1, [new(Currency.TRY, BanknoteDenomination.OneHundred, 4)])));

        using var response = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 100);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("withdrawal_stock_unavailable", await ReadProblemCodeAsync(response));
    }

    [Fact]
    public async Task Active_quote_reservation_is_not_offered_to_another_quote()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        using var first = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 100);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PostQuoteAsync(atmId, CardTwo, Currency.TRY, 100);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("withdrawal_stock_unavailable", await ReadProblemCodeAsync(second));
        using var balances = await GetBalancesAsync(atmId);
        AssertCassetteBalance(balances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 1, reserved: 1, available: 0);
    }

    [Fact]
    public async Task Cancel_releases_cash_and_daily_limit_and_is_safe_to_repeat()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();

        using var cancel = await PostCancelAsync(quoteId);
        using var cancelAgain = await PostCancelAsync(quoteId);
        using var cancelResult = await ParseAsync(cancel);
        using var secondCancelResult = await ParseAsync(cancelAgain);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("CANCELLED", cancelResult.RootElement.GetProperty("status").GetString());
        Assert.Equal("CANCELLED", secondCancelResult.RootElement.GetProperty("status").GetString());

        using var confirm = await PostConfirmAsync(quoteId, optionId, "cancelled-quote-confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        using var balances = await GetBalancesAsync(atmId);
        AssertCassetteBalance(balances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 1, reserved: 0, available: 1);
        Assert.Equal(0, (await GetDailyUsageAsync(CardOne, Currency.TRY)).ReservedAmount);
        Assert.Single(await GetMovementsAsync(atmId), movement => movement.Type == CassetteMovementType.ReservationRelease);
    }

    [Fact]
    public async Task Repeated_confirm_with_same_idempotency_key_returns_the_same_transaction_once()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();

        using var first = await PostConfirmAsync(quoteId, optionId, "idempotent-withdrawal-1");
        using var replay = await PostConfirmAsync(quoteId, optionId, "idempotent-withdrawal-1");
        using var firstResult = await ParseAsync(first);
        using var replayResult = await ParseAsync(replay);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstResult.RootElement.GetProperty("transactionId").GetGuid(), replayResult.RootElement.GetProperty("transactionId").GetGuid());
        Assert.Equal(1, await CountWithdrawalsAsync(quoteId));
        Assert.Single(await GetMovementsAsync(atmId), movement => movement.Type == CassetteMovementType.Withdrawal);
        using var balances = await GetBalancesAsync(atmId);
        AssertCassetteBalance(balances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 0, reserved: 0, available: 0);
        using var cancel = await PostCancelAsync(quoteId);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }

    [Fact]
    public async Task Idempotency_key_cannot_be_reused_for_different_confirmation_content()
    {
        var atmId = await CreateAtmAsync(InitialNotes(
            (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]),
            (3, [new(Currency.TRY, BanknoteDenomination.Fifty, 1)])));
        var firstQuote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var firstId = firstQuote.RootElement.GetProperty("quoteId").GetGuid();
        var firstOption = firstQuote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        firstQuote.Dispose();
        var secondQuote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 50);
        var secondId = secondQuote.RootElement.GetProperty("quoteId").GetGuid();
        var secondOption = secondQuote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        secondQuote.Dispose();

        using var first = await PostConfirmAsync(firstId, firstOption, "reused-idempotency-key");
        using var differentOption = await PostConfirmAsync(firstId, Guid.NewGuid(), "reused-idempotency-key");
        using var differentQuote = await PostConfirmAsync(secondId, secondOption, "reused-idempotency-key");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, differentOption.StatusCode);
        Assert.Equal("idempotency_key_conflict", await ReadProblemCodeAsync(differentOption));
        Assert.Equal(HttpStatusCode.Conflict, differentQuote.StatusCode);
        Assert.Equal("idempotency_key_conflict", await ReadProblemCodeAsync(differentQuote));
        Assert.Equal(1, await CountWithdrawalsAsync(firstId));
        Assert.Equal(0, await CountWithdrawalsAsync(secondId));
    }

    [Fact]
    public async Task Different_idempotency_key_cannot_withdraw_the_same_quote_twice()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();

        using var first = await PostConfirmAsync(quoteId, optionId, "confirm-first-key");
        using var second = await PostConfirmAsync(quoteId, optionId, "confirm-second-key");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await CountWithdrawalsAsync(quoteId));
    }

    [Fact]
    public async Task Confirm_requires_an_idempotency_key()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();

        using var response = await PostConfirmWithoutKeyAsync(quoteId, optionId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", await ReadProblemCodeAsync(response));
        Assert.Equal(0, await CountWithdrawalsAsync(quoteId));
    }

    [Fact]
    public async Task Expiration_worker_releases_note_and_daily_limit_reservations_without_waiting_sixty_seconds()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        quote.Dispose();
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));

        await Task.Delay(TimeSpan.FromMilliseconds(1_200));

        var storedQuote = await GetQuoteAsync(quoteId);
        Assert.Equal(WithdrawalQuoteStatus.Expired, storedQuote.Status);
        Assert.Equal(0, (await GetDailyUsageAsync(CardOne, Currency.TRY)).ReservedAmount);
        using var balances = await GetBalancesAsync(atmId);
        AssertCassetteBalance(balances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 1, reserved: 0, available: 1);
        Assert.Single(await GetMovementsAsync(atmId), movement => movement.Type == CassetteMovementType.ReservationRelease);
    }

    [Fact]
    public async Task Creating_quote_on_another_atm_releases_expired_card_limit_reservation()
    {
        var notes = InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]));
        var firstAtm = await CreateAtmAsync(notes);
        var secondAtm = await CreateAtmAsync(notes, resetDatabase: false);
        var firstQuote = await CreateQuoteDocumentAsync(firstAtm, CardOne, Currency.TRY, 100);
        var firstQuoteId = firstQuote.RootElement.GetProperty("quoteId").GetGuid();
        firstQuote.Dispose();
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));

        using var secondResponse = await PostQuoteAsync(secondAtm, CardOne, Currency.TRY, 100);
        using var secondQuote = await ParseAsync(secondResponse);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(WithdrawalQuoteStatus.Expired, (await GetQuoteAsync(firstQuoteId)).Status);
        Assert.Equal(100, (await GetDailyUsageAsync(CardOne, Currency.TRY)).ReservedAmount);
        using var firstBalances = await GetBalancesAsync(firstAtm);
        using var secondBalances = await GetBalancesAsync(secondAtm);
        AssertCassetteBalance(firstBalances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 1, reserved: 0, available: 1);
        AssertCassetteBalance(secondBalances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 1, reserved: 1, available: 0);
        Assert.Single(await GetMovementsAsync(firstAtm), movement => movement.Type == CassetteMovementType.ReservationRelease);
    }

    [Fact]
    public async Task Expired_quote_cannot_be_confirmed_and_api_expiration_is_idempotent()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));

        using var response = await PostConfirmAsync(quoteId, optionId, "expired-confirm-key");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(await ReadProblemCodeAsync(response), new[] { "withdrawal_quote_expired", "withdrawal_quote_not_active" });
        var storedQuote = await GetQuoteAsync(quoteId);
        Assert.Equal(WithdrawalQuoteStatus.Expired, storedQuote.Status);
        Assert.Equal(0, await CountWithdrawalsAsync(quoteId));
        Assert.Single(await GetMovementsAsync(atmId), movement => movement.Type == CassetteMovementType.ReservationRelease);
    }

    [Fact]
    public async Task Quote_expiry_stops_at_istanbul_midnight_and_next_day_gets_a_fresh_limit()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 2)])));
        fixture.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 20, 59, 30, TimeSpan.Zero));

        using var response = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 100);
        using var result = await ParseAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expiry = result.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromSeconds(30), expiry - fixture.Clock.UtcNow);
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<WithdrawalQuoteService>().ExpireDueQuotesAsync(CancellationToken.None);
        }

        var firstQuoteId = result.RootElement.GetProperty("quoteId").GetGuid();
        Assert.Equal(WithdrawalQuoteStatus.Expired, (await GetQuoteAsync(firstQuoteId)).Status);
        using var nextDay = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 100);
        Assert.Equal(HttpStatusCode.OK, nextDay.StatusCode);
    }

    [Fact]
    public async Task Daily_limits_are_separate_by_currency_and_confirmed_try_limit_is_enforced()
    {
        var configuration = Configuration(
            slot2Denomination: BanknoteDenomination.FiveHundred,
            slot3Currency: Currency.EUR,
            slot3Denomination: BanknoteDenomination.Twenty,
            slot5Currency: Currency.TRY,
            slot5Denomination: BanknoteDenomination.FiveHundred);
        var atmId = await CreateAtmAsync(InitialNotes(
            (2, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 40)]),
            (3, [new(Currency.EUR, BanknoteDenomination.Twenty, 100)]),
            (4, [new(Currency.USD, BanknoteDenomination.Twenty, 100)]),
            (5, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 40)])), configuration);

        await ConfirmExactAsync(atmId, CardOne, Currency.TRY, 20_000, "limit-try");
        await ConfirmExactAsync(atmId, CardOne, Currency.USD, 1_000, "limit-usd");
        await ConfirmExactAsync(atmId, CardOne, Currency.EUR, 1_000, "limit-eur");

        using var overTryLimit = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 500);
        using var overUsdLimit = await PostQuoteAsync(atmId, CardOne, Currency.USD, 20);
        using var overEurLimit = await PostQuoteAsync(atmId, CardOne, Currency.EUR, 20);
        Assert.Equal(HttpStatusCode.Conflict, overTryLimit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, overUsdLimit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, overEurLimit.StatusCode);
        Assert.Equal(20_000, (await GetDailyUsageAsync(CardOne, Currency.TRY)).ConfirmedAmount);
        Assert.Equal(1_000, (await GetDailyUsageAsync(CardOne, Currency.USD)).ConfirmedAmount);
        Assert.Equal(1_000, (await GetDailyUsageAsync(CardOne, Currency.EUR)).ConfirmedAmount);
        using var balances = await GetBalancesAsync(atmId);
        AssertCassetteBalance(balances.RootElement, 5, Currency.TRY, BanknoteDenomination.FiveHundred, physical: 40, reserved: 0, available: 40);
    }

    [Fact]
    public async Task Active_card_limit_reservation_is_shared_across_atms_and_cancel_frees_it()
    {
        var configuration = Configuration(
            slot2Denomination: BanknoteDenomination.FiveHundred,
            slot5Currency: Currency.TRY,
            slot5Denomination: BanknoteDenomination.FiveHundred);
        var initial = InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 40)]));
        var firstAtm = await CreateAtmAsync(initial, configuration);
        var secondAtm = await CreateAtmAsync(initial, configuration, resetDatabase: false);

        var quote = await CreateQuoteDocumentAsync(firstAtm, CardOne, Currency.TRY, 20_000);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        quote.Dispose();
        Assert.Equal(20_000, (await GetDailyUsageAsync(CardOne, Currency.TRY)).ReservedAmount);

        using var blocked = await PostQuoteAsync(secondAtm, CardOne, Currency.TRY, 500);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        using var cancel = await PostCancelAsync(quoteId);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(0, (await GetDailyUsageAsync(CardOne, Currency.TRY)).ReservedAmount);
        using var retry = await PostQuoteAsync(secondAtm, CardOne, Currency.TRY, 20_000);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Concurrent_cross_atm_quotes_cannot_exceed_the_same_cards_daily_limit()
    {
        var configuration = Configuration(
            slot2Denomination: BanknoteDenomination.FiveHundred,
            slot5Currency: Currency.TRY,
            slot5Denomination: BanknoteDenomination.FiveHundred);
        var initial = InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 24)]));
        var firstAtm = await CreateAtmAsync(initial, configuration);
        var secondAtm = await CreateAtmAsync(initial, configuration, resetDatabase: false);

        var responses = await Task.WhenAll(
            PostQuoteAsync(firstAtm, CardOne, Currency.TRY, 12_000),
            PostQuoteAsync(secondAtm, CardOne, Currency.TRY, 12_000));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var quotes = await Task.WhenAll(responses.Select(ParseAsync));
            try
            {
                var offered = quotes.Sum(quote => quote.RootElement.GetProperty("options").EnumerateArray()
                    .Max(option => option.GetProperty("amount").GetInt32()));
                Assert.Equal(20_000, offered);
                Assert.Equal(20_000, (await GetDailyUsageAsync(CardOne, Currency.TRY)).ReservedAmount);
            }
            finally
            {
                foreach (var quote in quotes)
                {
                    quote.Dispose();
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
    public async Task Card_numbers_are_only_stored_as_keyed_identifiers_and_are_not_returned()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        using var response = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 100);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(CardOne, body, StringComparison.Ordinal);
        using var quote = JsonDocument.Parse(body);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        using var confirm = await PostConfirmAsync(quoteId, optionId, "hash-card-confirm");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var expectedIdentifier = fixture.Services.GetRequiredService<CardIdentifierService>().CreateIdentifier(CardOne);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        var storedQuote = await db.WithdrawalQuotes.SingleAsync();
        var usage = await db.CardDailyLimitUsages.SingleAsync();
        Assert.Equal(expectedIdentifier, storedQuote.CardIdentifier);
        Assert.Equal(expectedIdentifier, usage.CardIdentifier);
        Assert.NotEqual(CardOne, storedQuote.CardIdentifier);
        Assert.Equal(expectedIdentifier, (await db.CashWithdrawals.SingleAsync()).CardIdentifier);
    }

    [Fact]
    public async Task Quote_and_confirm_are_rejected_when_atm_is_not_in_service()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])), inService: false);
        using var quoteResponse = await PostQuoteAsync(atmId, CardOne, Currency.TRY, 100);
        Assert.Equal(HttpStatusCode.Conflict, quoteResponse.StatusCode);
        Assert.Equal("invalid_atm_state", await ReadProblemCodeAsync(quoteResponse));

        using var serviceResponse = await SetStateAsync(atmId, AtmLifecycleState.InService);
        Assert.Equal(HttpStatusCode.OK, serviceResponse.StatusCode);
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();
        using var maintenance = await SetStateAsync(atmId, AtmLifecycleState.Maintenance);
        Assert.Equal(HttpStatusCode.OK, maintenance.StatusCode);
        using var confirm = await PostConfirmAsync(quoteId, optionId, "confirm-maintenance-state");
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal("invalid_atm_state", await ReadProblemCodeAsync(confirm));
        Assert.Equal(0, await CountWithdrawalsAsync(quoteId));
    }

    [Fact]
    public async Task Concurrent_quotes_on_one_atm_do_not_reserve_the_same_last_note()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var responses = await Task.WhenAll(
            PostQuoteAsync(atmId, CardOne, Currency.TRY, 100),
            PostQuoteAsync(atmId, CardTwo, Currency.TRY, 100));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            using var balances = await GetBalancesAsync(atmId);
            AssertCassetteBalance(balances.RootElement, 2, Currency.TRY, BanknoteDenomination.OneHundred, physical: 1, reserved: 1, available: 0);
            Assert.Equal(1, await CountActiveQuotesAsync(atmId));
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
    public async Task Concurrent_confirm_with_same_key_replays_one_transaction()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();
        var responses = await Task.WhenAll(
            PostConfirmAsync(quoteId, optionId, "parallel-idempotency-key"),
            PostConfirmAsync(quoteId, optionId, "parallel-idempotency-key"));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var results = await Task.WhenAll(responses.Select(ParseAsync));
            try
            {
                Assert.Equal(results[0].RootElement.GetProperty("transactionId").GetGuid(),
                    results[1].RootElement.GetProperty("transactionId").GetGuid());
                Assert.Equal(1, await CountWithdrawalsAsync(quoteId));
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
    public async Task Cancel_and_confirm_race_has_exactly_one_winner()
    {
        var atmId = await CreateAtmAsync(InitialNotes((2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)])));
        var quote = await CreateQuoteDocumentAsync(atmId, CardOne, Currency.TRY, 100);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var optionId = quote.RootElement.GetProperty("options")[0].GetProperty("optionId").GetGuid();
        quote.Dispose();

        var outcomes = await Task.WhenAll(
            PostConfirmAsync(quoteId, optionId, "race-confirm-key"),
            PostCancelAsync(quoteId));
        try
        {
            Assert.Equal(1, outcomes.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, outcomes.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var storedQuote = await GetQuoteAsync(quoteId);
            Assert.Contains(storedQuote.Status, new[] { WithdrawalQuoteStatus.Confirmed, WithdrawalQuoteStatus.Cancelled });
            Assert.Equal(storedQuote.Status == WithdrawalQuoteStatus.Confirmed ? 1 : 0, await CountWithdrawalsAsync(quoteId));
            using var balances = await GetBalancesAsync(atmId);
            Assert.Equal(0, balances.RootElement.GetProperty("cassettes")[1].GetProperty("reservedCount").GetInt32());
        }
        finally
        {
            foreach (var response in outcomes)
            {
                response.Dispose();
            }
        }
    }

    private async Task<Guid> CreateAtmAsync(
        IReadOnlyDictionary<int, BanknoteLoadRequest[]> initialNotes,
        ConfigureAtmRequest? configuration = null,
        bool inService = true,
        bool resetDatabase = true)
    {
        if (resetDatabase)
        {
            await fixture.ResetDatabaseAsync();
        }

        using var create = await fixture.Client.PostAsJsonAsync(
            "/api/atms",
            new CreateAtmRequest($"PHASE5-{Guid.NewGuid():N}"),
            JsonOptions);
        create.EnsureSuccessStatusCode();
        using var created = await ParseAsync(create);
        var atmId = created.RootElement.GetProperty("id").GetGuid();

        using var configure = await fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/configure",
            configuration ?? Configuration(),
            JsonOptions);
        configure.EnsureSuccessStatusCode();
        var loads = Enumerable.Range(1, 5).Select(number => new CassetteLoadRequest(
            number,
            initialNotes.TryGetValue(number, out var notes) ? notes : Array.Empty<BanknoteLoadRequest>())).ToArray();
        using var load = await fixture.Client.PostAsJsonAsync($"/api/atms/{atmId}/load", new LoadCashRequest(loads), JsonOptions);
        load.EnsureSuccessStatusCode();
        if (inService)
        {
            using var state = await SetStateAsync(atmId, AtmLifecycleState.InService);
            state.EnsureSuccessStatusCode();
        }

        return atmId;
    }

    private static ConfigureAtmRequest Configuration(
        Currency slot2Currency = Currency.TRY,
        BanknoteDenomination slot2Denomination = BanknoteDenomination.OneHundred,
        Currency slot3Currency = Currency.TRY,
        BanknoteDenomination slot3Denomination = BanknoteDenomination.Fifty,
        Currency slot4Currency = Currency.USD,
        BanknoteDenomination slot4Denomination = BanknoteDenomination.Twenty,
        Currency slot5Currency = Currency.EUR,
        BanknoteDenomination slot5Denomination = BanknoteDenomination.OneHundred) => new(
    [
        new CassetteDefinitionRequest(1, CassetteType.DepositOnly),
        new CassetteDefinitionRequest(2, CassetteType.Recycling, slot2Currency, slot2Denomination),
        new CassetteDefinitionRequest(3, CassetteType.Recycling, slot3Currency, slot3Denomination),
        new CassetteDefinitionRequest(4, CassetteType.Recycling, slot4Currency, slot4Denomination),
        new CassetteDefinitionRequest(5, CassetteType.Recycling, slot5Currency, slot5Denomination)
    ]);

    private static Dictionary<int, BanknoteLoadRequest[]> InitialNotes(
        params (int CassetteNumber, BanknoteLoadRequest[] Notes)[] overrides)
    {
        var result = new Dictionary<int, BanknoteLoadRequest[]>();
        foreach (var (number, notes) in overrides)
        {
            result[number] = notes;
        }

        return result;
    }

    private static WithdrawalQuoteRequest QuoteRequest(string cardNumber, Currency currency, int amount) =>
        new(cardNumber, currency, amount);

    private Task<HttpResponseMessage> PostQuoteAsync(Guid atmId, string cardNumber, Currency currency, int amount) =>
        fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/withdrawal-quotes",
            QuoteRequest(cardNumber, currency, amount),
            JsonOptions);

    private async Task<JsonDocument> CreateQuoteDocumentAsync(Guid atmId, string cardNumber, Currency currency, int amount)
    {
        var response = await PostQuoteAsync(atmId, cardNumber, currency, amount);
        try
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ParseAsync(response);
        }
        finally
        {
            response.Dispose();
        }
    }

    private Task<HttpResponseMessage> PostConfirmAsync(Guid quoteId, Guid optionId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/withdrawal-quotes/{quoteId}/confirm")
        {
            Content = JsonContent.Create(new ConfirmWithdrawalRequest(optionId), options: JsonOptions)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return fixture.Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostConfirmWithoutKeyAsync(Guid quoteId, Guid optionId) =>
        fixture.Client.PostAsJsonAsync(
            $"/api/withdrawal-quotes/{quoteId}/confirm",
            new ConfirmWithdrawalRequest(optionId),
            JsonOptions);

    private Task<HttpResponseMessage> PostCancelAsync(Guid quoteId) =>
        fixture.Client.PostAsync($"/api/withdrawal-quotes/{quoteId}/cancel", content: null);

    private Task<HttpResponseMessage> SetStateAsync(Guid atmId, AtmLifecycleState state) =>
        fixture.Client.PostAsJsonAsync($"/api/atms/{atmId}/state", new SetAtmStateRequest(state), JsonOptions);

    private async Task ConfirmExactAsync(Guid atmId, string cardNumber, Currency currency, int amount, string key)
    {
        using var quote = await CreateQuoteDocumentAsync(atmId, cardNumber, currency, amount);
        var quoteId = quote.RootElement.GetProperty("quoteId").GetGuid();
        var option = Assert.Single(quote.RootElement.GetProperty("options").EnumerateArray());
        Assert.Equal("EXACT", option.GetProperty("kind").GetString());
        using var response = await PostConfirmAsync(quoteId, option.GetProperty("optionId").GetGuid(), key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonDocument> GetBalancesAsync(Guid atmId)
    {
        using var response = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        response.EnsureSuccessStatusCode();
        return await ParseAsync(response);
    }

    private async Task<WithdrawalQuote> GetQuoteAsync(Guid quoteId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.WithdrawalQuotes.AsNoTracking().SingleAsync(quote => quote.Id == quoteId);
    }

    private async Task<List<QuoteReservation>> GetQuoteReservationsAsync(Guid quoteId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.QuoteReservations.AsNoTracking().Include(reservation => reservation.Cassette)
            .Where(reservation => reservation.QuoteId == quoteId).OrderBy(reservation => reservation.Cassette.Number).ToListAsync();
    }

    private async Task<CardDailyLimitUsage> GetDailyUsageAsync(string cardNumber, Currency currency)
    {
        var identifier = fixture.Services.GetRequiredService<CardIdentifierService>().CreateIdentifier(cardNumber);
        var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            fixture.Clock.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CardDailyLimitUsages.AsNoTracking().SingleAsync(usage =>
            usage.CardIdentifier == identifier && usage.Currency == currency && usage.BusinessDate == businessDate);
    }

    private async Task<List<CassetteMovement>> GetMovementsAsync(Guid atmId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CassetteMovements.AsNoTracking()
            .Where(movement => movement.AtmId == atmId)
            .OrderBy(movement => movement.OccurredAt).ThenBy(movement => movement.Type)
            .ToListAsync();
    }

    private async Task<int> CountWithdrawalsAsync(Guid quoteId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CashWithdrawals.CountAsync(withdrawal => withdrawal.QuoteId == quoteId);
    }

    private async Task<int> CountActiveQuotesAsync(Guid atmId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.WithdrawalQuotes.CountAsync(quote => quote.AtmId == atmId && quote.Status == WithdrawalQuoteStatus.Active);
    }

    private static void AssertCassetteBalance(
        JsonElement root,
        int cassetteNumber,
        Currency currency,
        BanknoteDenomination denomination,
        int physical,
        int reserved,
        int available)
    {
        var cassette = root.GetProperty("cassettes").EnumerateArray()
            .Single(item => item.GetProperty("number").GetInt32() == cassetteNumber);
        var balance = cassette.GetProperty("balances").EnumerateArray().Single(item =>
            item.GetProperty("currency").GetString() == currency.ToString() &&
            item.GetProperty("denomination").GetString() == ((int)denomination).ToString());
        Assert.Equal(physical, balance.GetProperty("physicalCount").GetInt32());
        Assert.Equal(reserved, balance.GetProperty("reservedCount").GetInt32());
        Assert.Equal(available, balance.GetProperty("availableCount").GetInt32());
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = await ParseAsync(response);
        return document.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }
}
