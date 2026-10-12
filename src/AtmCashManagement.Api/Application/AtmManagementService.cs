using System.Data;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtmCashManagement.Api.Application;

public sealed class AtmManagementService(AtmDbContext db, DepositAllocationPlanner depositAllocationPlanner)
{
    private const decimal ReconciliationApprovalThresholdTry = 1000m;

    public async Task<AtmResponse> CreateAtmAsync(CreateAtmRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Code))
        {
            throw AtmApiException.Validation("atm_code_required", "ATM code is required.");
        }

        if (request.Code.Trim().Length > 50)
        {
            throw AtmApiException.Validation("atm_code_too_long", "ATM code cannot exceed 50 characters.");
        }

        Atm atm;
        try
        {
            atm = new Atm(request.Code);
            db.Atms.Add(atm);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw AtmApiException.Conflict("atm_code_conflict", "An ATM with this code already exists.");
        }

        return ToAtmResponse(atm);
    }

    public async Task<IReadOnlyList<AtmResponse>> GetAtmsAsync(CancellationToken cancellationToken)
    {
        var atms = await db.Atms.AsNoTracking()
            .Include(atm => atm.Cassettes)
            .ThenInclude(cassette => cassette.Balances)
            .OrderBy(atm => atm.Code)
            .ToListAsync(cancellationToken);

        return atms.Select(ToAtmResponse).ToArray();
    }

    public async Task<AtmResponse> GetAtmAsync(Guid id, CancellationToken cancellationToken)
    {
        var atm = await db.Atms.AsNoTracking()
            .Include(item => item.Cassettes)
            .ThenInclude(cassette => cassette.Balances)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return atm is null ? throw AtmApiException.NotFound(id) : ToAtmResponse(atm);
    }

    public async Task<AtmResponse> ConfigureAsync(
        Guid id,
        ConfigureAtmRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw AtmApiException.Validation("configuration_required", "A cassette configuration request is required.");
        }

        var definitions = CassetteConfigurationValidator.Validate(request);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var atm = await LockAtmAsync(id, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(id);
        }

        if (!AtmLifecycleRules.CanConfigure(atm.State))
        {
            throw AtmApiException.Conflict("invalid_atm_state", $"ATM configuration is not allowed while the ATM is {atm.State}.");
        }

        await db.Entry(atm).Collection(item => item.Cassettes)
            .Query().Include(cassette => cassette.Balances).LoadAsync(cancellationToken);
        var existing = atm.Cassettes.OrderBy(cassette => cassette.Number).ToArray();

        if (atm.State == AtmLifecycleState.Unconfigured)
        {
            if (existing.Length != 0)
            {
                throw AtmApiException.Conflict("unconfigured_atm_has_cassettes", "An UNCONFIGURED ATM cannot already have cassette rows.");
            }

            foreach (var definition in definitions)
            {
                var cassette = new Cassette(
                    atm.Id,
                    definition.Number,
                    definition.Type,
                    definition.Currency,
                    definition.Denomination,
                    definition.Capacity);
                db.Cassettes.Add(cassette);
            }

            atm.MarkConfigured();
        }
        else
        {
            if (existing.Length != 5)
            {
                throw AtmApiException.Conflict("atm_configuration_invalid", "Maintenance configuration requires five existing cassette slots.");
            }

            var requestedByNumber = definitions.ToDictionary(definition => definition.Number);
            foreach (var cassette in existing)
            {
                var definition = requestedByNumber[cassette.Number];
                var changed = cassette.Type != definition.Type ||
                              cassette.Currency != definition.Currency ||
                              cassette.Denomination != definition.Denomination ||
                              cassette.Capacity != definition.Capacity;
                if (changed && cassette.Balances.Any(balance => balance.PhysicalCount > 0 || balance.ReservedCount > 0))
                {
                    throw AtmApiException.Conflict(
                        "cassette_contains_cash",
                        $"Cassette {cassette.Number} cannot be reconfigured while it contains physical or reserved notes.");
                }
            }

            foreach (var cassette in existing)
            {
                var definition = requestedByNumber[cassette.Number];
                var changed = cassette.Type != definition.Type ||
                              cassette.Currency != definition.Currency ||
                              cassette.Denomination != definition.Denomination ||
                              cassette.Capacity != definition.Capacity;
                if (changed)
                {
                    var previousBalances = cassette.Balances.ToArray();
                    db.CassetteBalances.RemoveRange(previousBalances);
                    cassette.Balances.Clear();
                }

                cassette.Reconfigure(definition.Type, definition.Currency, definition.Denomination, definition.Capacity);
            }

            atm.MarkConfigured();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToAtmResponse(atm);
    }

    public async Task<AtmResponse> LoadCashAsync(Guid id, LoadCashRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw AtmApiException.Validation("load_request_required", "A cash load request is required.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var atm = await LockAtmAsync(id, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(id);
        }

        if (!AtmLifecycleRules.CanLoadCash(atm.State))
        {
            throw AtmApiException.Conflict("invalid_atm_state", $"Cash loading is not allowed while the ATM is {atm.State}.");
        }

        var initialLoad = atm.State == AtmLifecycleState.Configured;
        if (!initialLoad && atm.InitialLoadCompletedAt is null)
        {
            throw AtmApiException.Conflict("initial_load_missing", "Maintenance loading requires a recorded initial load.");
        }

        await db.Entry(atm).Collection(item => item.Cassettes)
            .Query().Include(cassette => cassette.Balances).LoadAsync(cancellationToken);
        CassetteConfigurationValidator.ValidateStored(atm.Cassettes.ToArray());

        var cassetteLoads = request.Cassettes;
        if (cassetteLoads is null || cassetteLoads.Count == 0)
        {
            throw AtmApiException.Validation("load_lines_required", "At least one cassette must be included in a cash load request.");
        }

        if (cassetteLoads.Any(load => load is null || load.Notes is null))
        {
            throw AtmApiException.Validation("load_line_invalid", "Each cassette load must include a notes array.");
        }

        if (cassetteLoads.Select(load => load.CassetteNumber).Distinct().Count() != cassetteLoads.Count)
        {
            throw AtmApiException.Validation("duplicate_cassette_slot", "A cassette can appear only once in a load request.");
        }

        var cassettesByNumber = atm.Cassettes.ToDictionary(cassette => cassette.Number);
        if (initialLoad &&
            (cassetteLoads.Count != 5 || cassetteLoads.Select(load => load.CassetteNumber).Order().Where((number, index) => number != index + 1).Any()))
        {
            throw AtmApiException.Validation(
                "initial_load_incomplete",
                "The initial load request must explicitly include cassette slots 1 through 5. A cassette may have an empty notes array.");
        }

        var lines = new List<ValidatedLoadLine>();
        foreach (var cassetteLoad in cassetteLoads)
        {
            if (!cassettesByNumber.TryGetValue(cassetteLoad.CassetteNumber, out var cassette))
            {
                throw AtmApiException.Validation("cassette_not_configured", $"Cassette {cassetteLoad.CassetteNumber} is not configured for this ATM.");
            }

            var notes = cassetteLoad.Notes!;
            if (notes.Any(note => note is null))
            {
                throw AtmApiException.Validation("load_line_invalid", $"Cassette {cassette.Number} contains a null note entry.");
            }

            if (notes.GroupBy(note => (note.Currency, note.Denomination)).Any(group => group.Count() > 1))
            {
                throw AtmApiException.Validation("duplicate_note_type", $"A currency and denomination can appear only once in cassette {cassette.Number}.");
            }

            long noteTotal = 0;
            foreach (var note in notes)
            {
                if (!Enum.IsDefined(note.Currency) || !Enum.IsDefined(note.Denomination))
                {
                    throw AtmApiException.Validation("note_type_invalid", "Only TRY, USD, EUR and denominations 20, 50, 100, 500 are supported.");
                }

                if (note.Count < 0)
                {
                    throw AtmApiException.Validation("note_count_invalid", "Loaded note counts cannot be negative.");
                }

                if (cassette.Type == CassetteType.Recycling &&
                    (cassette.Currency != note.Currency || cassette.Denomination != note.Denomination))
                {
                    throw AtmApiException.Validation(
                        "recycling_note_mismatch",
                        $"Recycling cassette {cassette.Number} accepts only {cassette.Currency} {cassette.Denomination} banknotes.");
                }

                noteTotal += note.Count;
                lines.Add(new ValidatedLoadLine(cassette, note));
            }

            var existingPhysicalCount = cassette.Balances.Sum(balance => balance.PhysicalCount);
            if (existingPhysicalCount + noteTotal > cassette.Capacity)
            {
                throw AtmApiException.Conflict(
                    "cassette_capacity_exceeded",
                    $"Loading cassette {cassette.Number} would exceed its capacity of {cassette.Capacity} banknotes.");
            }
        }

        if (lines.Sum(line => (long)line.Note.Count) <= 0)
        {
            throw AtmApiException.Validation("load_amount_empty", "A cash load must include at least one banknote.");
        }

        var reference = Guid.NewGuid().ToString("N");
        var occurredAt = DateTimeOffset.UtcNow;
        foreach (var line in lines.Where(line => line.Note.Count > 0))
        {
            var balance = line.Cassette.Balances.SingleOrDefault(item =>
                item.Currency == line.Note.Currency && item.Denomination == line.Note.Denomination);
            if (balance is null)
            {
                balance = new CassetteBalance(
                    line.Cassette.Id,
                    line.Note.Currency,
                    line.Note.Denomination,
                    physicalCount: line.Note.Count);
                db.CassetteBalances.Add(balance);
            }
            else
            {
                balance.AddPhysicalNotes(line.Note.Count);
            }

            db.CassetteMovements.Add(new CassetteMovement(
                atm.Id,
                line.Cassette.Id,
                CassetteMovementType.Load,
                line.Note.Currency,
                line.Note.Denomination,
                physicalCountDelta: line.Note.Count,
                reservedCountDelta: 0,
                occurredAt,
                reference,
                isInitialLoad: initialLoad));
        }

        if (initialLoad)
        {
            atm.CompleteInitialLoad(occurredAt);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToAtmResponse(atm);
    }

    public async Task<CashDepositResponse> DepositAsync(
        Guid id,
        CashDepositRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw AtmApiException.Validation("deposit_request_required", "A deposit request is required.");
        }

        if (request.UnrecognizedNoteCount < 0)
        {
            throw AtmApiException.Validation("unrecognized_note_count_invalid", "Unrecognized note count cannot be negative.");
        }

        var notes = request.Notes;
        if (notes is null || notes.Any(note => note is null))
        {
            throw AtmApiException.Validation("deposit_notes_required", "Deposit notes must be supplied as an array.");
        }

        if (notes.GroupBy(note => (note.Currency, note.Denomination)).Any(group => group.Count() > 1))
        {
            throw AtmApiException.Validation("duplicate_note_type", "A deposit may include each currency and denomination only once.");
        }

        foreach (var note in notes)
        {
            if (!Enum.IsDefined(note.Currency) || !Enum.IsDefined(note.Denomination))
            {
                throw AtmApiException.Validation("note_type_invalid", "Only TRY, USD, EUR and denominations 20, 50, 100, 500 are supported.");
            }

            if (note.Count < 0)
            {
                throw AtmApiException.Validation("note_count_invalid", "Deposited note counts cannot be negative.");
            }
        }

        if (request.UnrecognizedNoteCount == 0 && notes.Sum(note => (long)note.Count) == 0)
        {
            throw AtmApiException.Validation("deposit_empty", "A deposit must contain at least one recognized or unrecognized note.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var atm = await LockAtmAsync(id, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(id);
        }

        if (!AtmLifecycleRules.CanPerformCustomerCashOperation(atm.State))
        {
            throw AtmApiException.Conflict("invalid_atm_state", $"Deposits are not allowed while the ATM is {atm.State}.");
        }

        await db.Entry(atm).Collection(item => item.Cassettes)
            .Query().Include(cassette => cassette.Balances).LoadAsync(cancellationToken);
        CassetteConfigurationValidator.ValidateStored(atm.Cassettes.ToArray());

        var cassettes = atm.Cassettes.OrderBy(cassette => cassette.Number).ToArray();
        var cassettesByNumber = cassettes.ToDictionary(cassette => cassette.Number);
        var plan = depositAllocationPlanner.Plan(
            cassettes.Select(cassette => new CassetteCapacitySnapshot(
                cassette.Number,
                cassette.Type,
                cassette.Capacity,
                cassette.Currency,
                cassette.Denomination,
                cassette.Balances.Sum(balance => balance.PhysicalCount))).ToArray(),
            notes);

        var occurredAt = DateTimeOffset.UtcNow;
        var cashDeposit = new CashDeposit(atm.Id, occurredAt, request.UnrecognizedNoteCount);
        foreach (var note in notes.Where(note => note.Count > 0))
        {
            var acceptedCount = plan.Placements
                .Where(placement => placement.Currency == note.Currency && placement.Denomination == note.Denomination)
                .Sum(placement => placement.Count);
            var returnedCount = plan.CapacityReturns
                .Where(returned => returned.Currency == note.Currency && returned.Denomination == note.Denomination)
                .Sum(returned => returned.Count);
            cashDeposit.AddNote(new CashDepositNote(
                cashDeposit.Id,
                note.Currency,
                note.Denomination,
                note.Count,
                acceptedCount,
                returnedCount));
        }

        foreach (var placement in plan.Placements)
        {
            var cassette = cassettesByNumber[placement.CassetteNumber];
            var balance = cassette.Balances.SingleOrDefault(item =>
                item.Currency == placement.Currency && item.Denomination == placement.Denomination);
            if (balance is null)
            {
                balance = new CassetteBalance(
                    cassette.Id,
                    placement.Currency,
                    placement.Denomination,
                    physicalCount: placement.Count);
                db.CassetteBalances.Add(balance);
            }
            else
            {
                balance.AddPhysicalNotes(placement.Count);
            }

            db.CassetteMovements.Add(new CassetteMovement(
                atm.Id,
                cassette.Id,
                CassetteMovementType.Deposit,
                placement.Currency,
                placement.Denomination,
                physicalCountDelta: placement.Count,
                reservedCountDelta: 0,
                occurredAt,
                cashDeposit.Id.ToString("N")));
        }

        db.CashDeposits.Add(cashDeposit);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var acceptedNotes = plan.Placements
            .GroupBy(placement => (placement.Currency, placement.Denomination))
            .Select(group => new DepositAcceptedNoteResponse(group.Key.Currency, group.Key.Denomination, group.Sum(placement => placement.Count)))
            .OrderBy(note => note.Currency)
            .ThenBy(note => note.Denomination)
            .ToArray();
        var acceptedValues = Enum.GetValues<Currency>()
            .Select(currency => new CurrencyBalanceResponse(
                currency,
                plan.Placements.Where(placement => placement.Currency == currency)
                    .Sum(placement => (long)placement.Count * (int)placement.Denomination)))
            .ToArray();

        return new CashDepositResponse(
            cashDeposit.Id,
            atm.Id,
            occurredAt,
            acceptedNotes,
            plan.Placements,
            plan.CapacityReturns,
            request.UnrecognizedNoteCount,
            acceptedValues);
    }

    public async Task<CashReconciliationResponse> ReconcileAsync(
        Guid id,
        CashReconciliationRequest request,
        string? createdBy,
        CancellationToken cancellationToken)
    {
        var operatorName = RequireReconciliationUser(createdBy);
        if (request is null || request.Cassettes is null || request.Cassettes.Count != 5)
        {
            throw AtmApiException.Validation("reconciliation_snapshot_incomplete", "A reconciliation snapshot must include all five cassette slots.");
        }

        if (request.Cassettes.Any(cassette => cassette is null || cassette.Notes is null))
        {
            throw AtmApiException.Validation("reconciliation_snapshot_invalid", "Each cassette snapshot must include a notes array.");
        }

        if (request.Cassettes.Any(cassette => cassette.CassetteNumber is < 1 or > 5) ||
            request.Cassettes.Select(cassette => cassette.CassetteNumber).Distinct().Count() != 5 ||
            request.Cassettes.Select(cassette => cassette.CassetteNumber).Order().Where((number, index) => number != index + 1).Any())
        {
            throw AtmApiException.Validation("reconciliation_snapshot_incomplete", "Snapshot cassette numbers must uniquely cover slots 1 through 5.");
        }

        foreach (var cassette in request.Cassettes)
        {
            var notes = cassette.Notes!;
            if (notes.Any(note => note is null))
            {
                throw AtmApiException.Validation("reconciliation_note_invalid", $"Cassette {cassette.CassetteNumber} contains a null note entry.");
            }

            if (notes.GroupBy(note => (note.Currency, note.Denomination)).Any(group => group.Count() > 1))
            {
                throw AtmApiException.Validation("duplicate_note_type", $"Cassette {cassette.CassetteNumber} has duplicate count lines.");
            }

            foreach (var note in notes)
            {
                if (!Enum.IsDefined(note.Currency) || !Enum.IsDefined(note.Denomination))
                {
                    throw AtmApiException.Validation("note_type_invalid", "Only TRY, USD, EUR and denominations 20, 50, 100, 500 are supported.");
                }

                if (note.Count < 0)
                {
                    throw AtmApiException.Validation("note_count_invalid", "Physical note counts cannot be negative.");
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var atm = await LockAtmAsync(id, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(id);
        }

        if (!AtmLifecycleRules.CanReconcile(atm.State))
        {
            throw AtmApiException.Conflict("invalid_atm_state", $"Reconciliation is only allowed in MAINTENANCE, not {atm.State}.");
        }

        if (await db.CashReconciliations.AnyAsync(
                reconciliation => reconciliation.AtmId == atm.Id &&
                                  reconciliation.Status == CashReconciliationStatus.AwaitingApproval,
                cancellationToken))
        {
            throw AtmApiException.Conflict(
                "reconciliation_approval_required",
                "This ATM already has a reconciliation awaiting approval.");
        }

        await db.Entry(atm).Collection(item => item.Cassettes)
            .Query().Include(cassette => cassette.Balances).LoadAsync(cancellationToken);
        CassetteConfigurationValidator.ValidateStored(atm.Cassettes.ToArray());
        var cassettes = atm.Cassettes.OrderBy(cassette => cassette.Number).ToArray();
        var snapshotsByNumber = request.Cassettes.ToDictionary(cassette => cassette.CassetteNumber);

        if (cassettes.Any(cassette => cassette.Balances.Any(balance => balance.ReservedCount > 0)))
        {
            throw AtmApiException.Conflict(
                "reconciliation_active_reservations",
                "Reconciliation is blocked while any cassette has reserved notes; release or resolve those reservations first.");
        }

        foreach (var cassette in cassettes)
        {
            var countedNotes = snapshotsByNumber[cassette.Number].Notes!;
            if (cassette.Type == CassetteType.Recycling && countedNotes.Any(note =>
                    note.Currency != cassette.Currency || note.Denomination != cassette.Denomination))
            {
                throw AtmApiException.Validation(
                    "recycling_note_mismatch",
                    $"Recycling cassette {cassette.Number} can contain only {cassette.Currency} {cassette.Denomination} banknotes.");
            }

            if (countedNotes.Sum(note => (long)note.Count) > cassette.Capacity)
            {
                throw AtmApiException.Validation(
                    "cassette_capacity_exceeded",
                    $"The physical count for cassette {cassette.Number} exceeds its capacity of {cassette.Capacity} notes.");
            }
        }

        var ratesToTry = await LoadExchangeRatesToTryAsync(cancellationToken);
        var occurredAt = DateTimeOffset.UtcNow;
        var reconciliation = new CashReconciliation(
            atm.Id,
            occurredAt,
            operatorName,
            ratesToTry[Currency.TRY],
            ratesToTry[Currency.USD],
            ratesToTry[Currency.EUR]);
        var responseByCassette = new List<ReconciliationCassetteResult>();
        var changedCassettes = new List<ReconciliationCassetteChange>();
        var differenceValues = Enum.GetValues<Currency>().ToDictionary(currency => currency, _ => 0L);

        foreach (var cassette in cassettes)
        {
            var countedByType = snapshotsByNumber[cassette.Number].Notes!
                .ToDictionary(note => (note.Currency, note.Denomination), note => note.Count);
            var existingByType = cassette.Balances.ToDictionary(balance => (balance.Currency, balance.Denomination));
            var noteTypes = existingByType.Keys.Concat(countedByType.Keys).Distinct().ToList();
            if (cassette.Type == CassetteType.Recycling)
            {
                noteTypes.Add((cassette.Currency!.Value, cassette.Denomination!.Value));
            }

            var noteResults = new List<ReconciliationNoteResult>();
            foreach (var noteType in noteTypes.Distinct().OrderBy(note => note.Currency).ThenBy(note => note.Denomination))
            {
                existingByType.TryGetValue(noteType, out var existingBalance);
                var expectedCount = existingBalance?.PhysicalCount ?? 0;
                var countedCount = countedByType.GetValueOrDefault(noteType);
                var differenceCount = countedCount - expectedCount;
                var line = new CashReconciliationLine(
                    reconciliation.Id,
                    cassette.Id,
                    noteType.Currency,
                    noteType.Denomination,
                    expectedCount,
                    countedCount);
                reconciliation.AddLine(line);
                noteResults.Add(new ReconciliationNoteResult(
                    noteType.Currency,
                    noteType.Denomination,
                    expectedCount,
                    countedCount,
                    differenceCount));
                differenceValues[noteType.Currency] += (long)differenceCount * (int)noteType.Denomination;

                if (differenceCount == 0)
                {
                    continue;
                }

                if (existingBalance is null)
                {
                    existingBalance = new CassetteBalance(
                        cassette.Id,
                        noteType.Currency,
                        noteType.Denomination,
                        physicalCount: countedCount);
                    db.CassetteBalances.Add(existingBalance);
                }
                else
                {
                    existingBalance.ReconcilePhysicalCount(countedCount);
                }

                db.CassetteMovements.Add(new CassetteMovement(
                    atm.Id,
                    cassette.Id,
                    CassetteMovementType.ReconciliationAdjustment,
                    noteType.Currency,
                    noteType.Denomination,
                    physicalCountDelta: differenceCount,
                    reservedCountDelta: 0,
                    occurredAt,
                    reconciliation.Id.ToString("N")));
            }

            responseByCassette.Add(new ReconciliationCassetteResult(
                cassette.Number,
                noteResults.OrderBy(note => note.Currency).ThenBy(note => note.Denomination).ToArray()));

            var cassetteChanges = noteResults.Where(note => note.DifferenceCount != 0).ToArray();
            if (cassetteChanges.Length > 0)
            {
                changedCassettes.Add(new ReconciliationCassetteChange(
                    cassette.Number,
                    cassetteChanges.Length,
                    cassetteChanges.Sum(note => note.DifferenceCount)));
            }
        }

        var totalAbsoluteDifferenceTry = ReconciliationDifferenceCalculator.CalculateAbsoluteDifferenceTry(
            reconciliation.Lines,
            ratesToTry);
        reconciliation.SetValuation(totalAbsoluteDifferenceTry);
        if (totalAbsoluteDifferenceTry > ReconciliationApprovalThresholdTry)
        {
            reconciliation.RequireApproval();
        }

        db.CashReconciliations.Add(reconciliation);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CashReconciliationResponse(
            reconciliation.Id,
            atm.Id,
            occurredAt,
            responseByCassette,
            differenceValues.Select(pair => new CurrencyDifferenceResponse(pair.Key, pair.Value)).ToArray(),
            changedCassettes,
            reconciliation.Status,
            reconciliation.CreatedBy,
            reconciliation.ApprovedBy,
            reconciliation.ApprovedAt,
            reconciliation.TotalAbsoluteDifferenceTry,
            reconciliation.TryToTryRateSnapshot,
            reconciliation.UsdToTryRateSnapshot,
            reconciliation.EurToTryRateSnapshot);
    }

    public async Task<ReconciliationApprovalResponse> ApproveReconciliationAsync(
        Guid atmId,
        Guid reconciliationId,
        string? approvedBy,
        CancellationToken cancellationToken)
    {
        var approver = RequireReconciliationUser(approvedBy);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var atm = await LockAtmAsync(atmId, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(atmId);
        }

        var reconciliation = await db.CashReconciliations
            .FromSqlInterpolated($"SELECT * FROM cash_reconciliations WHERE \"Id\" = {reconciliationId} AND \"AtmId\" = {atmId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (reconciliation is null)
        {
            throw AtmApiException.NotFound(
                "reconciliation_not_found",
                "Reconciliation not found",
                $"Reconciliation '{reconciliationId}' was not found for ATM '{atmId}'.");
        }

        if (reconciliation.Status == CashReconciliationStatus.Approved)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToReconciliationApprovalResponse(reconciliation);
        }

        if (reconciliation.Status != CashReconciliationStatus.AwaitingApproval)
        {
            throw AtmApiException.Conflict(
                "reconciliation_not_awaiting_approval",
                "Only a reconciliation awaiting approval can be approved.");
        }

        if (string.Equals(reconciliation.CreatedBy, approver, StringComparison.OrdinalIgnoreCase))
        {
            throw AtmApiException.Conflict(
                "reconciliation_self_approval",
                "A different user must approve this reconciliation.");
        }

        reconciliation.Approve(approver, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToReconciliationApprovalResponse(reconciliation);
    }

    public async Task<AtmResponse> SetStateAsync(
        Guid id,
        SetAtmStateRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw AtmApiException.Validation("state_request_required", "A lifecycle state is required.");
        }

        if (!Enum.IsDefined(request.State))
        {
            throw AtmApiException.Validation("atm_state_invalid", "ATM lifecycle state is not supported.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var atm = await LockAtmAsync(id, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(id);
        }

        if (!AtmLifecycleRules.IsValidTransition(atm.State, request.State))
        {
            throw AtmApiException.Conflict(
                "invalid_state_transition",
                $"The transition from {atm.State} to {request.State} is not allowed.");
        }

        if (request.State == AtmLifecycleState.InService &&
            await db.CashReconciliations.AnyAsync(
                reconciliation => reconciliation.AtmId == atm.Id &&
                                  reconciliation.Status == CashReconciliationStatus.AwaitingApproval,
                cancellationToken))
        {
            throw AtmApiException.Conflict(
                "reconciliation_approval_required",
                "The ATM cannot return to service while a reconciliation is awaiting approval.");
        }

        if (request.State is AtmLifecycleState.Configured or AtmLifecycleState.InService)
        {
            await db.Entry(atm).Collection(item => item.Cassettes)
                .Query().Include(cassette => cassette.Balances).LoadAsync(cancellationToken);
            CassetteConfigurationValidator.ValidateStored(atm.Cassettes.ToArray());
        }

        if (request.State is AtmLifecycleState.Loaded or AtmLifecycleState.InService)
        {
            var initialLoadExists = atm.InitialLoadCompletedAt is not null &&
                                    await db.CassetteMovements.AnyAsync(
                                        movement => movement.AtmId == atm.Id &&
                                                    movement.Type == CassetteMovementType.Load &&
                                                    movement.IsInitialLoad,
                                        cancellationToken);
            if (!initialLoadExists)
            {
                throw AtmApiException.Conflict(
                    "initial_load_missing",
                    "The ATM cannot enter this state until a successful initial cash load is recorded.");
            }
        }

        atm.TransitionTo(request.State);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToAtmResponse(atm);
    }

    public async Task<AtmBalanceResponse> GetBalancesAsync(Guid id, CancellationToken cancellationToken)
    {
        var atm = await db.Atms.AsNoTracking()
            .Include(item => item.Cassettes)
            .ThenInclude(cassette => cassette.Balances)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (atm is null)
        {
            throw AtmApiException.NotFound(id);
        }

        var cassetteResponses = atm.Cassettes.OrderBy(cassette => cassette.Number).Select(ToCassetteResponse).ToArray();
        var totals = Enum.GetValues<Currency>()
            .Select(currency => new CurrencyBalanceResponse(
                currency,
                atm.Cassettes.SelectMany(cassette => cassette.Balances)
                    .Where(balance => balance.Currency == currency)
                    .Sum(balance => (long)balance.PhysicalCount * (int)balance.Denomination)))
            .ToArray();

        return new AtmBalanceResponse(atm.Id, atm.Code, atm.State, cassetteResponses, totals);
    }

    private async Task<Atm?> LockAtmAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Atms
            .FromSqlInterpolated($"SELECT * FROM atms WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<Currency, decimal>> LoadExchangeRatesToTryAsync(CancellationToken cancellationToken)
    {
        var rates = await db.ExchangeRates.AsNoTracking()
            .Where(rate => rate.QuoteCurrency == Currency.TRY)
            .ToListAsync(cancellationToken);
        var ratesByCurrency = rates.ToDictionary(rate => rate.BaseCurrency, rate => rate.Rate);
        foreach (var currency in Enum.GetValues<Currency>())
        {
            if (!ratesByCurrency.TryGetValue(currency, out var rate) || rate <= 0m)
            {
                throw AtmApiException.Conflict(
                    "exchange_rate_missing",
                    $"A positive {currency}-to-TRY exchange rate is required for reconciliation.");
            }
        }

        if (ratesByCurrency[Currency.TRY] != 1m)
        {
            throw AtmApiException.Conflict(
                "exchange_rate_invalid",
                "The TRY-to-TRY exchange rate must equal 1.");
        }

        return ratesByCurrency;
    }

    private static string RequireReconciliationUser(string? user)
    {
        var normalized = user?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw AtmApiException.Validation("reconciliation_user_required", "The X-User header is required for reconciliation operations.");
        }

        if (normalized.Length > 100)
        {
            throw AtmApiException.Validation("reconciliation_user_invalid", "The X-User value cannot exceed 100 characters.");
        }

        return normalized;
    }

    private static ReconciliationApprovalResponse ToReconciliationApprovalResponse(CashReconciliation reconciliation) => new(
        reconciliation.Id,
        reconciliation.AtmId,
        reconciliation.Status,
        reconciliation.CreatedBy,
        reconciliation.ApprovedBy,
        reconciliation.ApprovedAt,
        reconciliation.TotalAbsoluteDifferenceTry);

    private static AtmResponse ToAtmResponse(Atm atm) => new(
        atm.Id,
        atm.Code,
        atm.State,
        atm.CreatedAt,
        atm.InitialLoadCompletedAt,
        atm.Cassettes.OrderBy(cassette => cassette.Number).Select(ToCassetteResponse).ToArray());

    private static CassetteResponse ToCassetteResponse(Cassette cassette)
    {
        var balances = cassette.Balances
            .OrderBy(balance => balance.Currency)
            .ThenBy(balance => balance.Denomination)
            .Select(balance => new CassetteBalanceResponse(
                balance.Currency,
                balance.Denomination,
                balance.PhysicalCount,
                balance.ReservedCount,
                balance.AvailableCount))
            .ToArray();

        return new CassetteResponse(
            cassette.Id,
            cassette.Number,
            cassette.Type,
            cassette.Currency,
            cassette.Denomination,
            cassette.Capacity,
            balances.Sum(balance => balance.PhysicalCount),
            balances.Sum(balance => balance.ReservedCount),
            balances.Sum(balance => balance.AvailableCount),
            balances);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record ValidatedLoadLine(Cassette Cassette, BanknoteLoadRequest Note);
}
