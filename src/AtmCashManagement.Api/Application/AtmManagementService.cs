using System.Data;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtmCashManagement.Api.Application;

public sealed class AtmManagementService(AtmDbContext db)
{
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
