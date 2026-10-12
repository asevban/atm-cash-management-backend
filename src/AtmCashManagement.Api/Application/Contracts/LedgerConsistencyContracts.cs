using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

public sealed record LedgerConsistencyResponse(
    Guid AtmId,
    bool IsConsistent,
    int CheckedCassetteCount,
    int CheckedBalanceCount,
    int InconsistentBalanceCount,
    IReadOnlyList<string> Inconsistencies,
    IReadOnlyList<CassetteLedgerConsistencyResponse> Cassettes);

public sealed record CassetteLedgerConsistencyResponse(
    Guid CassetteId,
    int CassetteNumber,
    CassetteType Type,
    bool IsConsistent,
    IReadOnlyList<CassetteNoteLedgerConsistencyResponse> Balances);

public sealed record CassetteNoteLedgerConsistencyResponse(
    Currency Currency,
    BanknoteDenomination Denomination,
    long DatabasePhysicalCount,
    long LedgerPhysicalCount,
    long DatabaseReservedCount,
    long LedgerReservedCount,
    long PhysicalDifference,
    long ReservedDifference,
    long DatabaseAvailableCount,
    long LedgerAvailableCount,
    long AvailableDifference,
    bool IsConsistent);
