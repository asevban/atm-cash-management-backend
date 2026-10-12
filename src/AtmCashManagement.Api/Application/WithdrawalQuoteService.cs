using System.Data;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AtmCashManagement.Api.Application;

public sealed class WithdrawalQuoteService(
    AtmDbContext db,
    INoteSelectionService noteSelection,
    CardIdentifierService cardIdentifierService,
    IClock clock,
    LowCashEventRecorder lowCashEventRecorder)
{
    private static readonly TimeZoneInfo IstanbulTimeZone = GetIstanbulTimeZone();
    private static readonly IReadOnlyDictionary<Currency, int> DailyLimits = new Dictionary<Currency, int>
    {
        [Currency.TRY] = 20_000,
        [Currency.USD] = 1_000,
        [Currency.EUR] = 1_000
    };

    public async Task<WithdrawalQuoteResponse> CreateQuoteAsync(
        Guid atmId,
        WithdrawalQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw AtmApiException.Validation("withdrawal_quote_required", "A withdrawal quote request is required.");
        }

        if (!IsValidCardNumber(request.CardNumber))
        {
            throw AtmApiException.Validation("card_number_invalid", "Card number must contain 12 to 19 digits.");
        }

        if (!Enum.IsDefined(request.Currency))
        {
            throw AtmApiException.Validation("currency_invalid", "Only TRY, USD and EUR are supported.");
        }

        if (request.RequestedAmount <= 0)
        {
            throw AtmApiException.Validation("withdrawal_amount_invalid", "Requested amount must be positive.");
        }

        var cardIdentifier = cardIdentifierService.CreateIdentifier(request.CardNumber!);
        var now = UtcNow();
        var businessDate = GetBusinessDate(now);
        var requestedLimitKey = new DailyLimitLockKey(cardIdentifier, request.Currency, businessDate);
        var expiredCandidates = await db.WithdrawalQuotes.AsNoTracking()
            .Where(quote => quote.Status == WithdrawalQuoteStatus.Active && quote.ExpiresAtUtc <= now &&
                (quote.AtmId == atmId ||
                 (quote.CardIdentifier == cardIdentifier && quote.Currency == request.Currency && quote.LimitBusinessDate == businessDate)))
            .OrderBy(quote => quote.AtmId)
            .ThenBy(quote => quote.Id)
            .Select(quote => new ExpiredQuoteCandidate(
                quote.Id,
                quote.AtmId,
                quote.CardIdentifier,
                quote.Currency,
                quote.LimitBusinessDate))
            .ToListAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var lockedUsages = new Dictionary<DailyLimitLockKey, CardDailyLimitUsage>();
        var expiredKeys = expiredCandidates
            .Select(candidate => new DailyLimitLockKey(candidate.CardIdentifier, candidate.Currency, candidate.LimitBusinessDate));
        foreach (var key in expiredKeys.Append(requestedLimitKey).Distinct().OrderBy(key => key, DailyLimitLockKeyComparer.Instance))
        {
            lockedUsages[key] = await EnsureAndLockDailyUsageAsync(key, cancellationToken);
        }

        var lockedAtms = new Dictionary<Guid, Atm>();
        var atmIdsToLock = expiredCandidates.Select(candidate => candidate.AtmId).Append(atmId).Distinct().Order().ToArray();
        foreach (var candidateAtmId in atmIdsToLock)
        {
            var lockedAtm = await LockAtmAsync(candidateAtmId, cancellationToken);
            if (lockedAtm is null && candidateAtmId == atmId)
            {
                throw AtmApiException.NotFound(atmId);
            }

            if (lockedAtm is not null)
            {
                lockedAtms[candidateAtmId] = lockedAtm;
            }
        }

        var atm = lockedAtms[atmId];
        foreach (var lockedAtm in lockedAtms.Values)
        {
            await LoadCassettesAndBalancesAsync(lockedAtm, cancellationToken);
        }

        CassetteConfigurationValidator.ValidateStored(atm.Cassettes.ToArray());
        var expiredQuoteIds = expiredCandidates.Select(candidate => candidate.QuoteId).ToArray();
        var expiredQuotes = await LockQuotesAsync(expiredQuoteIds, cancellationToken);
        var expiredAny = false;
        foreach (var expiredQuote in expiredQuotes)
        {
            if (expiredQuote.Status != WithdrawalQuoteStatus.Active || expiredQuote.ExpiresAtUtc > now)
            {
                continue;
            }

            var key = new DailyLimitLockKey(expiredQuote.CardIdentifier, expiredQuote.Currency, expiredQuote.LimitBusinessDate);
            if (!lockedUsages.TryGetValue(key, out var usage))
            {
                throw new InvalidOperationException("An expired quote's daily limit row was not locked.");
            }

            var expiredAtm = lockedAtms[expiredQuote.AtmId];
            var cassettesById = expiredAtm.Cassettes.ToDictionary(cassette => cassette.Id);
            await ReleaseQuoteReservationsAsync(expiredQuote, usage, cassettesById, now, expiredQuote.Id.ToString("N"), cancellationToken);
            expiredQuote.Expire(now);
            expiredAny = true;
        }

        if (!AtmLifecycleRules.CanPerformCustomerCashOperation(atm.State))
        {
            await RejectAfterExpirationAsync(
                transaction,
                expiredAny,
                AtmApiException.Conflict("invalid_atm_state", $"Withdrawal quotes are not allowed while the ATM is {atm.State}."),
                cancellationToken);
        }

        var usageForCard = lockedUsages[requestedLimitKey];
        var dailyLimit = DailyLimits[request.Currency];
        var remainingLimit = Math.Max(0, dailyLimit - usageForCard.ConfirmedAmount - usageForCard.ReservedAmount);
        var cassetteStocks = atm.Cassettes.Select(cassette => new CassetteStockInput(
            cassette.Number,
            cassette.Type,
            cassette.Capacity,
            cassette.Currency,
            cassette.Denomination,
            cassette.Balances.Sum(balance => balance.PhysicalCount),
            cassette.Balances.Sum(balance => balance.ReservedCount))).ToArray();

        var selection = noteSelection.Select(request.Currency, request.RequestedAmount, cassetteStocks, remainingLimit);
        var optionPlans = BuildOptionPlans(selection);
        if (optionPlans.Count == 0)
        {
            var failure = selection.Outcome == NoteSelectionOutcome.NoAvailableCash
                ? AtmApiException.Conflict("withdrawal_stock_unavailable", "No matching recycling-cassette notes are available for this currency.")
                : AtmApiException.Conflict("withdrawal_no_permitted_payout", "No payable amount is available within the remaining daily limit.");
            await RejectAfterExpirationAsync(transaction, expiredAny, failure, cancellationToken);
        }

        var limitReservedAmount = optionPlans.Max(plan => plan.Option.Amount);
        try
        {
            usageForCard.Reserve(limitReservedAmount, dailyLimit);
        }
        catch (InvalidOperationException)
        {
            await RejectAfterExpirationAsync(
                transaction,
                expiredAny,
                AtmApiException.Conflict("daily_limit_exceeded", "The card's remaining daily limit cannot cover the offered options."),
                cancellationToken);
        }

        var expiresAtUtc = GetExpiry(now);
        var quote = new WithdrawalQuote(
            atm.Id,
            cardIdentifier,
            request.Currency,
            request.RequestedAmount,
            businessDate,
            limitReservedAmount,
            now,
            expiresAtUtc);
        db.WithdrawalQuotes.Add(quote);

        foreach (var plan in optionPlans)
        {
            var option = new WithdrawalQuoteOption(
                quote.Id,
                plan.Kind,
                request.Currency,
                plan.Option.Amount,
                plan.Option.NoteCount);
            quote.AddOption(option);
            foreach (var allocation in plan.Option.Allocations.OrderBy(item => item.CassetteNumber)
                         .ThenBy(item => item.Denomination))
            {
                var cassette = atm.Cassettes.Single(item => item.Number == allocation.CassetteNumber);
                option.AddAllocation(new WithdrawalQuoteOptionAllocation(
                    option.Id,
                    cassette.Id,
                    allocation.Currency,
                    allocation.Denomination,
                    allocation.Count));
            }
        }

        var reservations = BuildCombinedReservations(optionPlans);
        foreach (var reservation in reservations.OrderBy(item => item.CassetteNumber).ThenBy(item => item.Denomination))
        {
            var cassette = atm.Cassettes.Single(item => item.Number == reservation.CassetteNumber);
            var balance = cassette.Balances.Single(item =>
                item.Currency == request.Currency && item.Denomination == reservation.Denomination);
            var previousAvailable = balance.AvailableCount;
            balance.ReserveNotes(reservation.Count);
            lowCashEventRecorder.RecordIfThresholdCrossed(cassette, balance, previousAvailable, now, quote.Id.ToString("N"));
            quote.AddReservation(new QuoteReservation(
                quote.Id,
                cassette.Id,
                request.Currency,
                reservation.Denomination,
                reservation.Count));
            db.CassetteMovements.Add(new CassetteMovement(
                atm.Id,
                cassette.Id,
                CassetteMovementType.Reservation,
                request.Currency,
                reservation.Denomination,
                physicalCountDelta: 0,
                reservedCountDelta: reservation.Count,
                now,
                quote.Id.ToString("N")));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToQuoteResponse(quote, atm.Id);
    }

    public async Task<WithdrawalConfirmationResponse> ConfirmAsync(
        Guid quoteId,
        ConfirmWithdrawalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (request is null || request.SelectedOptionId == Guid.Empty)
        {
            throw AtmApiException.Validation("withdrawal_option_required", "A selected quote option is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw AtmApiException.Validation("idempotency_key_required", "A non-empty Idempotency-Key header of at most 200 characters is required.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({idempotencyKey}, 0))",
            cancellationToken);

        var priorRequest = await db.ConfirmIdempotencyRecords.AsNoTracking()
            .Include(record => record.Withdrawal)
            .ThenInclude(withdrawal => withdrawal.Option)
            .ThenInclude(option => option.Allocations)
            .ThenInclude(allocation => allocation.Cassette)
            .SingleOrDefaultAsync(record => record.Key == idempotencyKey, cancellationToken);
        if (priorRequest is not null)
        {
            if (priorRequest.QuoteId != quoteId || priorRequest.SelectedOptionId != request.SelectedOptionId)
            {
                throw AtmApiException.Conflict("idempotency_key_conflict", "This Idempotency-Key was already used for a different confirmation request.");
            }

            await transaction.CommitAsync(cancellationToken);
            return ToConfirmationResponse(priorRequest.Withdrawal);
        }

        var metadata = await GetQuoteMetadataAsync(quoteId, cancellationToken);
        if (metadata is null)
        {
            throw QuoteNotFound(quoteId);
        }

        var usageKey = new DailyLimitLockKey(metadata.CardIdentifier, metadata.Currency, metadata.LimitBusinessDate);
        var usage = await EnsureAndLockDailyUsageAsync(usageKey, cancellationToken);
        var atm = await LockAtmAsync(metadata.AtmId, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(metadata.AtmId);
        }

        var quote = await LockQuoteAsync(quoteId, cancellationToken);
        if (quote is null)
        {
            throw QuoteNotFound(quoteId);
        }

        var now = UtcNow();
        if (quote.Status != WithdrawalQuoteStatus.Active)
        {
            throw AtmApiException.Conflict("withdrawal_quote_not_active", $"A quote in {quote.Status} state cannot be confirmed.");
        }

        if (now >= quote.ExpiresAtUtc || GetBusinessDate(now) != quote.LimitBusinessDate)
        {
            await LoadCassettesAndBalancesAsync(atm, cancellationToken);
            var cassetteMap = atm.Cassettes.ToDictionary(cassette => cassette.Id);
            await ReleaseQuoteReservationsAsync(quote, usage, cassetteMap, now, quote.Id.ToString("N"), cancellationToken);
            quote.Expire(now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw AtmApiException.Conflict("withdrawal_quote_expired", "The quote has expired and its reservations were released.");
        }

        if (!AtmLifecycleRules.CanPerformCustomerCashOperation(atm.State))
        {
            throw AtmApiException.Conflict("invalid_atm_state", $"Withdrawal confirmation is not allowed while the ATM is {atm.State}.");
        }

        await LoadCassettesAndBalancesAsync(atm, cancellationToken);
        var option = await db.WithdrawalQuoteOptions
            .Include(item => item.Allocations)
            .ThenInclude(allocation => allocation.Cassette)
            .SingleOrDefaultAsync(item => item.Id == request.SelectedOptionId && item.QuoteId == quote.Id, cancellationToken);
        if (option is null)
        {
            throw AtmApiException.Validation("withdrawal_option_invalid", "The selected option does not belong to this quote.");
        }

        var reservations = await db.QuoteReservations
            .Where(reservation => reservation.QuoteId == quote.Id)
            .OrderBy(reservation => reservation.CassetteId)
            .ThenBy(reservation => reservation.Currency)
            .ThenBy(reservation => reservation.Denomination)
            .ToListAsync(cancellationToken);
        var selectedCounts = option.Allocations.ToDictionary(
            allocation => (allocation.CassetteId, allocation.Currency, allocation.Denomination),
            allocation => allocation.Count);
        var transactionId = Guid.NewGuid();
        foreach (var reservation in reservations)
        {
            var key = (reservation.CassetteId, reservation.Currency, reservation.Denomination);
            var selectedCount = selectedCounts.GetValueOrDefault(key);
            if (selectedCount > reservation.ReservedCount)
            {
                throw new InvalidOperationException("The selected payout exceeds the quote's combined reservation.");
            }

            var cassette = atm.Cassettes.Single(item => item.Id == reservation.CassetteId);
            var balance = cassette.Balances.Single(item =>
                item.Currency == reservation.Currency && item.Denomination == reservation.Denomination);
            var unusedCount = reservation.ReservedCount - selectedCount;

            if (selectedCount > 0)
            {
                var previousAvailable = balance.AvailableCount;
                balance.ConsumeReservedNotes(selectedCount);
                lowCashEventRecorder.RecordIfThresholdCrossed(cassette, balance, previousAvailable, now, transactionId.ToString("N"));
                db.CassetteMovements.Add(new CassetteMovement(
                    atm.Id,
                    cassette.Id,
                    CassetteMovementType.Withdrawal,
                    reservation.Currency,
                    reservation.Denomination,
                    physicalCountDelta: -selectedCount,
                    reservedCountDelta: -selectedCount,
                    now,
                    transactionId.ToString("N")));
            }

            if (unusedCount > 0)
            {
                var previousAvailable = balance.AvailableCount;
                balance.ReleaseReservedNotes(unusedCount);
                lowCashEventRecorder.RecordIfThresholdCrossed(cassette, balance, previousAvailable, now, transactionId.ToString("N"));
                db.CassetteMovements.Add(new CassetteMovement(
                    atm.Id,
                    cassette.Id,
                    CassetteMovementType.ReservationRelease,
                    reservation.Currency,
                    reservation.Denomination,
                    physicalCountDelta: 0,
                    reservedCountDelta: -unusedCount,
                    now,
                    transactionId.ToString("N")));
            }
        }

        usage.ConfirmReservation(quote.LimitReservedAmount, option.Amount);
        quote.Confirm(now);
        var withdrawal = new CashWithdrawal(
            transactionId,
            atm.Id,
            quote.Id,
            option.Id,
            quote.CardIdentifier,
            quote.Currency,
            option.Amount,
            now);
        db.CashWithdrawals.Add(withdrawal);
        db.ConfirmIdempotencyRecords.Add(new ConfirmIdempotencyRecord(
            idempotencyKey,
            quote.Id,
            option.Id,
            withdrawal.Id,
            now));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToConfirmationResponse(withdrawal, option);
    }

    public async Task<WithdrawalQuoteStatusResponse> CancelAsync(Guid quoteId, CancellationToken cancellationToken)
    {
        var metadata = await GetQuoteMetadataAsync(quoteId, cancellationToken);
        if (metadata is null)
        {
            throw QuoteNotFound(quoteId);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var usageKey = new DailyLimitLockKey(metadata.CardIdentifier, metadata.Currency, metadata.LimitBusinessDate);
        var usage = await EnsureAndLockDailyUsageAsync(usageKey, cancellationToken);
        var atm = await LockAtmAsync(metadata.AtmId, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(metadata.AtmId);
        }

        var quote = await LockQuoteAsync(quoteId, cancellationToken);
        if (quote is null)
        {
            throw QuoteNotFound(quoteId);
        }

        if (quote.Status == WithdrawalQuoteStatus.Confirmed)
        {
            throw AtmApiException.Conflict("withdrawal_quote_confirmed", "A confirmed quote cannot be cancelled.");
        }

        if (quote.Status is WithdrawalQuoteStatus.Cancelled or WithdrawalQuoteStatus.Expired)
        {
            await transaction.CommitAsync(cancellationToken);
            return new WithdrawalQuoteStatusResponse(quote.Id, quote.Status, quote.ExpiresAtUtc);
        }

        var now = UtcNow();
        await LoadCassettesAndBalancesAsync(atm, cancellationToken);
        var cassetteMap = atm.Cassettes.ToDictionary(cassette => cassette.Id);
        var expired = now >= quote.ExpiresAtUtc || GetBusinessDate(now) != quote.LimitBusinessDate;
        await ReleaseQuoteReservationsAsync(quote, usage, cassetteMap, now, quote.Id.ToString("N"), cancellationToken);
        if (expired)
        {
            quote.Expire(now);
        }
        else
        {
            quote.Cancel(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WithdrawalQuoteStatusResponse(quote.Id, quote.Status, quote.ExpiresAtUtc);
    }

    public async Task<int> ExpireDueQuotesAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var candidates = await db.WithdrawalQuotes.AsNoTracking()
            .Where(quote => quote.Status == WithdrawalQuoteStatus.Active && quote.ExpiresAtUtc <= now)
            .OrderBy(quote => quote.AtmId)
            .ThenBy(quote => quote.CardIdentifier)
            .ThenBy(quote => quote.Currency)
            .ThenBy(quote => quote.LimitBusinessDate)
            .ThenBy(quote => quote.Id)
            .Select(quote => new ExpiredQuoteCandidate(
                quote.Id,
                quote.AtmId,
                quote.CardIdentifier,
                quote.Currency,
                quote.LimitBusinessDate))
            .Take(200)
            .ToListAsync(cancellationToken);

        var expiredCount = 0;
        foreach (var candidate in candidates)
        {
            if (await ExpireCandidateAsync(candidate, now, cancellationToken))
            {
                expiredCount++;
            }
        }

        return expiredCount;
    }

    private async Task<bool> ExpireCandidateAsync(
        ExpiredQuoteCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var key = new DailyLimitLockKey(candidate.CardIdentifier, candidate.Currency, candidate.LimitBusinessDate);
        var usage = await EnsureAndLockDailyUsageAsync(key, cancellationToken);
        var atm = await LockAtmAsync(candidate.AtmId, cancellationToken);
        if (atm is null)
        {
            return false;
        }

        var quote = await LockQuoteAsync(candidate.QuoteId, cancellationToken);
        if (quote is null || quote.Status != WithdrawalQuoteStatus.Active || quote.ExpiresAtUtc > now)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await LoadCassettesAndBalancesAsync(atm, cancellationToken);
        var cassetteMap = atm.Cassettes.ToDictionary(cassette => cassette.Id);
        await ReleaseQuoteReservationsAsync(quote, usage, cassetteMap, now, quote.Id.ToString("N"), cancellationToken);
        quote.Expire(now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task ReleaseQuoteReservationsAsync(
        WithdrawalQuote quote,
        CardDailyLimitUsage usage,
        IReadOnlyDictionary<Guid, Cassette> cassettesById,
        DateTimeOffset now,
        string transactionReference,
        CancellationToken cancellationToken)
    {
        var reservations = await db.QuoteReservations
            .Where(reservation => reservation.QuoteId == quote.Id)
            .OrderBy(reservation => reservation.CassetteId)
            .ThenBy(reservation => reservation.Currency)
            .ThenBy(reservation => reservation.Denomination)
            .ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var cassette = cassettesById[reservation.CassetteId];
            var balance = cassette.Balances.Single(item =>
                item.Currency == reservation.Currency && item.Denomination == reservation.Denomination);
            var previousAvailable = balance.AvailableCount;
            balance.ReleaseReservedNotes(reservation.ReservedCount);
            lowCashEventRecorder.RecordIfThresholdCrossed(cassette, balance, previousAvailable, now, transactionReference);
            db.CassetteMovements.Add(new CassetteMovement(
                quote.AtmId,
                cassette.Id,
                CassetteMovementType.ReservationRelease,
                reservation.Currency,
                reservation.Denomination,
                physicalCountDelta: 0,
                reservedCountDelta: -reservation.ReservedCount,
                now,
                transactionReference));
        }

        usage.Release(quote.LimitReservedAmount);
    }

    private async Task<CardDailyLimitUsage> EnsureAndLockDailyUsageAsync(
        DailyLimitLockKey key,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO card_daily_limit_usages (\"Id\", \"CardIdentifier\", \"Currency\", \"BusinessDate\", \"ConfirmedAmount\", \"ReservedAmount\") VALUES ({Guid.NewGuid()}, {key.CardIdentifier}, {key.Currency.ToString()}, {key.BusinessDate}, 0, 0) ON CONFLICT (\"CardIdentifier\", \"Currency\", \"BusinessDate\") DO NOTHING",
            cancellationToken);

        return await db.CardDailyLimitUsages
            .FromSqlInterpolated(
                $"SELECT * FROM card_daily_limit_usages WHERE \"CardIdentifier\" = {key.CardIdentifier} AND \"Currency\" = {key.Currency.ToString()} AND \"BusinessDate\" = {key.BusinessDate} FOR UPDATE")
            .SingleAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<WithdrawalQuote>> LockQuotesAsync(
        Guid[] quoteIds,
        CancellationToken cancellationToken) =>
        quoteIds.Length == 0
            ? []
            : await db.WithdrawalQuotes
                .FromSqlInterpolated(
                    $"SELECT * FROM withdrawal_quotes WHERE \"Id\" = ANY ({quoteIds}) ORDER BY \"AtmId\", \"Id\" FOR UPDATE")
                .ToListAsync(cancellationToken);

    private async Task<Atm?> LockAtmAsync(Guid atmId, CancellationToken cancellationToken) =>
        await db.Atms
            .FromSqlInterpolated($"SELECT * FROM atms WHERE \"Id\" = {atmId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<WithdrawalQuote?> LockQuoteAsync(Guid quoteId, CancellationToken cancellationToken) =>
        await db.WithdrawalQuotes
            .FromSqlInterpolated($"SELECT * FROM withdrawal_quotes WHERE \"Id\" = {quoteId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<QuoteMetadata?> GetQuoteMetadataAsync(Guid quoteId, CancellationToken cancellationToken) =>
        await db.WithdrawalQuotes.AsNoTracking()
            .Where(quote => quote.Id == quoteId)
            .Select(quote => new QuoteMetadata(
                quote.Id,
                quote.AtmId,
                quote.CardIdentifier,
                quote.Currency,
                quote.LimitBusinessDate))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task LoadCassettesAndBalancesAsync(Atm atm, CancellationToken cancellationToken) =>
        await db.Entry(atm).Collection(item => item.Cassettes)
            .Query().Include(cassette => cassette.Balances).LoadAsync(cancellationToken);

    private async Task RejectAfterExpirationAsync(
        IDbContextTransaction transaction,
        bool expiredAny,
        AtmApiException exception,
        CancellationToken cancellationToken)
    {
        if (expiredAny)
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        throw exception;
    }

    private static List<PlannedOption> BuildOptionPlans(NoteSelectionResult selection)
    {
        var options = new List<PlannedOption>();
        if (selection.Exact is not null)
        {
            options.Add(new PlannedOption(WithdrawalOptionKind.Exact, selection.Exact));
        }
        else
        {
            if (selection.Lower is not null)
            {
                options.Add(new PlannedOption(WithdrawalOptionKind.Lower, selection.Lower));
            }

            if (selection.Upper is not null)
            {
                options.Add(new PlannedOption(WithdrawalOptionKind.Upper, selection.Upper));
            }
        }

        return options;
    }

    private static IReadOnlyList<CombinedReservation> BuildCombinedReservations(IReadOnlyList<PlannedOption> options) =>
        options.SelectMany(plan => plan.Option.Allocations
                .GroupBy(allocation => (allocation.CassetteNumber, allocation.Currency, allocation.Denomination))
                .Select(group => new CombinedReservation(group.Key.CassetteNumber, group.Key.Currency, group.Key.Denomination, group.Sum(item => item.Count))))
            .GroupBy(reservation => (reservation.CassetteNumber, reservation.Currency, reservation.Denomination))
            .Select(group => new CombinedReservation(
                group.Key.CassetteNumber,
                group.Key.Currency,
                group.Key.Denomination,
                group.Max(reservation => reservation.Count)))
            .ToArray();

    private static WithdrawalQuoteResponse ToQuoteResponse(WithdrawalQuote quote, Guid atmId) => new(
        quote.Id,
        atmId,
        quote.RequestedAmount,
        quote.Currency,
        quote.ExpiresAtUtc,
        quote.Status,
        quote.Options.OrderBy(option => option.Kind)
            .Select(option => new WithdrawalQuoteOptionResponse(
                option.Id,
                option.Kind,
                option.Currency,
                option.Amount,
                option.NoteCount,
                option.Allocations.OrderBy(allocation => allocation.Cassette.Number)
                    .ThenBy(allocation => allocation.Denomination)
                    .Select(allocation => new CassetteNoteAllocationResponse(
                        allocation.Cassette.Number,
                        allocation.Currency,
                        allocation.Denomination,
                        allocation.Count))
                    .ToArray()))
            .ToArray());

    private static WithdrawalConfirmationResponse ToConfirmationResponse(CashWithdrawal withdrawal) =>
        ToConfirmationResponse(withdrawal, withdrawal.Option);

    private static WithdrawalConfirmationResponse ToConfirmationResponse(CashWithdrawal withdrawal, WithdrawalQuoteOption option) => new(
        withdrawal.Id,
        withdrawal.QuoteId,
        withdrawal.Currency,
        withdrawal.Amount,
        withdrawal.OccurredAtUtc,
        option.Allocations.OrderBy(allocation => allocation.Cassette.Number)
            .ThenBy(allocation => allocation.Denomination)
            .Select(allocation => new CassetteNoteAllocationResponse(
                allocation.Cassette.Number,
                allocation.Currency,
                allocation.Denomination,
                allocation.Count))
            .ToArray());

    private static bool IsValidCardNumber(string? cardNumber) =>
        cardNumber is { Length: >= 12 and <= 19 } && cardNumber.All(character => character is >= '0' and <= '9');

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("IClock.UtcNow must return a UTC timestamp.");
        }

        return now;
    }

    private static DateOnly GetBusinessDate(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, IstanbulTimeZone).DateTime);

    private static DateTimeOffset GetExpiry(DateTimeOffset now)
    {
        var localDate = GetBusinessDate(now);
        var nextLocalMidnight = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, IstanbulTimeZone);
        var localDayEnd = new DateTimeOffset(nextMidnightUtc, TimeSpan.Zero);
        var sixtySeconds = now.AddSeconds(60);
        return localDayEnd < sixtySeconds ? localDayEnd : sixtySeconds;
    }

    private static TimeZoneInfo GetIstanbulTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }

    private static AtmApiException QuoteNotFound(Guid quoteId) =>
        new(StatusCodes.Status404NotFound, "withdrawal_quote_not_found", "Withdrawal quote not found", $"Withdrawal quote '{quoteId}' was not found.");

    private sealed record PlannedOption(WithdrawalOptionKind Kind, NoteSelectionOption Option);
    private sealed record CombinedReservation(int CassetteNumber, Currency Currency, BanknoteDenomination Denomination, int Count);
    private sealed record QuoteMetadata(Guid Id, Guid AtmId, string CardIdentifier, Currency Currency, DateOnly LimitBusinessDate);
    private sealed record ExpiredQuoteCandidate(Guid QuoteId, Guid AtmId, string CardIdentifier, Currency Currency, DateOnly LimitBusinessDate);
    private readonly record struct DailyLimitLockKey(string CardIdentifier, Currency Currency, DateOnly BusinessDate);

    private sealed class DailyLimitLockKeyComparer : IComparer<DailyLimitLockKey>
    {
        public static DailyLimitLockKeyComparer Instance { get; } = new();

        public int Compare(DailyLimitLockKey left, DailyLimitLockKey right)
        {
            var cardComparison = StringComparer.Ordinal.Compare(left.CardIdentifier, right.CardIdentifier);
            if (cardComparison != 0)
            {
                return cardComparison;
            }

            var currencyComparison = left.Currency.CompareTo(right.Currency);
            return currencyComparison != 0 ? currencyComparison : left.BusinessDate.CompareTo(right.BusinessDate);
        }
    }
}
