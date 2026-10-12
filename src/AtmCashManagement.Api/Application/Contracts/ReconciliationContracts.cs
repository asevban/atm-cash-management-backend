using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

public sealed record CashReconciliationRequest(IReadOnlyList<CassetteCountSnapshotRequest>? Cassettes);

public sealed record CassetteCountSnapshotRequest(
    int CassetteNumber,
    IReadOnlyList<BanknoteLoadRequest>? Notes);

public sealed record ReconciliationNoteResult(
    Currency Currency,
    BanknoteDenomination Denomination,
    int ExpectedCount,
    int CountedCount,
    int DifferenceCount);

public sealed record ReconciliationCassetteResult(
    int CassetteNumber,
    IReadOnlyList<ReconciliationNoteResult> Notes);

public sealed record ReconciliationCassetteChange(
    int CassetteNumber,
    int ChangedNoteTypes,
    int NetNoteCountDifference);

public sealed record CurrencyDifferenceResponse(Currency Currency, long DifferenceValue);

public sealed record CashReconciliationResponse(
    Guid ReconciliationId,
    Guid AtmId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<ReconciliationCassetteResult> Cassettes,
    IReadOnlyList<CurrencyDifferenceResponse> DifferenceValueByCurrency,
    IReadOnlyList<ReconciliationCassetteChange> ChangedCassettes);
