using System.Data;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmCashManagement.Api.Application;

public sealed class CassetteLedgerConsistencyService(AtmDbContext db)
{
    public async Task<LedgerConsistencyResponse> CheckAsync(Guid atmId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        var atmExists = await db.Atms.AsNoTracking().AnyAsync(atm => atm.Id == atmId, cancellationToken);
        if (!atmExists)
        {
            throw AtmApiException.NotFound(atmId);
        }

        var cassettes = await db.Cassettes.AsNoTracking()
            .Where(cassette => cassette.AtmId == atmId)
            .OrderBy(cassette => cassette.Number)
            .Select(cassette => new CassetteSnapshot(cassette.Id, cassette.Number, cassette.Type))
            .ToListAsync(cancellationToken);

        var balances = await db.CassetteBalances.AsNoTracking()
            .Where(balance => balance.Cassette.AtmId == atmId)
            .Select(balance => new BalanceSnapshot(
                balance.CassetteId,
                balance.Currency,
                balance.Denomination,
                balance.PhysicalCount,
                balance.ReservedCount))
            .ToListAsync(cancellationToken);

        var ledger = await db.CassetteMovements.AsNoTracking()
            .Where(movement => movement.AtmId == atmId)
            .GroupBy(movement => new { movement.CassetteId, movement.Currency, movement.Denomination })
            .Select(group => new LedgerSnapshot(
                group.Key.CassetteId,
                group.Key.Currency,
                group.Key.Denomination,
                group.Sum(movement => (long)movement.PhysicalCountDelta),
                group.Sum(movement => (long)movement.ReservedCountDelta)))
            .ToListAsync(cancellationToken);

        var balancesByKey = balances.ToDictionary(
            balance => (balance.CassetteId, balance.Currency, balance.Denomination));
        var ledgerByKey = ledger.ToDictionary(
            movement => (movement.CassetteId, movement.Currency, movement.Denomination));
        var cassetteResults = new List<CassetteLedgerConsistencyResponse>(cassettes.Count);
        var inconsistencies = new List<string>();
        var inconsistentBalanceCount = 0;

        foreach (var cassette in cassettes)
        {
            var cassetteKeys = balancesByKey.Keys
                .Where(key => key.CassetteId == cassette.Id)
                .Concat(ledgerByKey.Keys.Where(key => key.CassetteId == cassette.Id))
                .Distinct()
                .OrderBy(key => key.Currency)
                .ThenBy(key => key.Denomination)
                .ToArray();
            var noteResults = new List<CassetteNoteLedgerConsistencyResponse>(cassetteKeys.Length);

            foreach (var key in cassetteKeys)
            {
                balancesByKey.TryGetValue(key, out var databaseBalance);
                ledgerByKey.TryGetValue(key, out var ledgerBalance);
                var databasePhysical = (long)(databaseBalance?.PhysicalCount ?? 0);
                var databaseReserved = (long)(databaseBalance?.ReservedCount ?? 0);
                var ledgerPhysical = ledgerBalance?.PhysicalCount ?? 0;
                var ledgerReserved = ledgerBalance?.ReservedCount ?? 0;
                var physicalDifference = databasePhysical - ledgerPhysical;
                var reservedDifference = databaseReserved - ledgerReserved;
                var availableDifference = (databasePhysical - databaseReserved) - (ledgerPhysical - ledgerReserved);
                var isConsistent = physicalDifference == 0 && reservedDifference == 0 &&
                                   databasePhysical >= 0 && databaseReserved >= 0 && databaseReserved <= databasePhysical &&
                                   ledgerPhysical >= 0 && ledgerReserved >= 0 && ledgerReserved <= ledgerPhysical;

                noteResults.Add(new CassetteNoteLedgerConsistencyResponse(
                    key.Currency,
                    key.Denomination,
                    databasePhysical,
                    ledgerPhysical,
                    databaseReserved,
                    ledgerReserved,
                    physicalDifference,
                    reservedDifference,
                    databasePhysical - databaseReserved,
                    ledgerPhysical - ledgerReserved,
                    availableDifference,
                    isConsistent));

                if (!isConsistent)
                {
                    inconsistentBalanceCount++;
                    inconsistencies.Add(
                        $"Cassette {cassette.Number}, {key.Currency} {key.Denomination}: " +
                        $"database physical/reserved {databasePhysical}/{databaseReserved}, " +
                        $"ledger physical/reserved {ledgerPhysical}/{ledgerReserved}.");
                }
            }

            cassetteResults.Add(new CassetteLedgerConsistencyResponse(
                cassette.Id,
                cassette.Number,
                cassette.Type,
                noteResults.All(note => note.IsConsistent),
                noteResults));
        }

        return new LedgerConsistencyResponse(
            atmId,
            inconsistentBalanceCount == 0,
            cassettes.Count,
            cassetteResults.Sum(cassette => cassette.Balances.Count),
            inconsistentBalanceCount,
            inconsistencies,
            cassetteResults);
    }

    private sealed record CassetteSnapshot(Guid Id, int Number, CassetteType Type);
    private sealed record BalanceSnapshot(
        Guid CassetteId,
        Currency Currency,
        BanknoteDenomination Denomination,
        int PhysicalCount,
        int ReservedCount);
    private sealed record LedgerSnapshot(
        Guid CassetteId,
        Currency Currency,
        BanknoteDenomination Denomination,
        long PhysicalCount,
        long ReservedCount);
}
