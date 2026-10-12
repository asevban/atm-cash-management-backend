using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

public sealed record CashDepositRequest(
    IReadOnlyList<BanknoteLoadRequest>? Notes,
    int UnrecognizedNoteCount);

public sealed record DepositAcceptedNoteResponse(
    Currency Currency,
    BanknoteDenomination Denomination,
    int Count);

public sealed record DepositPlacementResponse(
    int CassetteNumber,
    Currency Currency,
    BanknoteDenomination Denomination,
    int Count);

public sealed record DepositCapacityReturnResponse(
    Currency Currency,
    BanknoteDenomination Denomination,
    int Count);

public sealed record CashDepositResponse(
    Guid TransactionId,
    Guid AtmId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<DepositAcceptedNoteResponse> AcceptedNotes,
    IReadOnlyList<DepositPlacementResponse> Placements,
    IReadOnlyList<DepositCapacityReturnResponse> CapacityReturns,
    int UnrecognizedNoteCount,
    IReadOnlyList<CurrencyBalanceResponse> AcceptedValueByCurrency);

public sealed record CassetteCapacitySnapshot(
    int CassetteNumber,
    CassetteType Type,
    int Capacity,
    Currency? Currency,
    BanknoteDenomination? Denomination,
    int PhysicalCount);

public sealed record DepositAllocationPlan(
    IReadOnlyList<DepositPlacementResponse> Placements,
    IReadOnlyList<DepositCapacityReturnResponse> CapacityReturns);
