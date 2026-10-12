using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

public sealed record WithdrawalQuoteRequest(string? CardNumber, Currency Currency, int RequestedAmount);

public sealed record WithdrawalQuoteOptionResponse(
    Guid OptionId,
    WithdrawalOptionKind Kind,
    Currency Currency,
    int Amount,
    int NoteCount,
    IReadOnlyList<CassetteNoteAllocationResponse> Allocations);

public sealed record CassetteNoteAllocationResponse(
    int CassetteNumber,
    Currency Currency,
    BanknoteDenomination Denomination,
    int Count);

public sealed record WithdrawalQuoteResponse(
    Guid QuoteId,
    Guid AtmId,
    int RequestedAmount,
    Currency Currency,
    DateTimeOffset ExpiresAtUtc,
    WithdrawalQuoteStatus Status,
    IReadOnlyList<WithdrawalQuoteOptionResponse> Options);

public sealed record ConfirmWithdrawalRequest(Guid SelectedOptionId);

public sealed record WithdrawalConfirmationResponse(
    Guid TransactionId,
    Guid QuoteId,
    Currency Currency,
    int Amount,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<CassetteNoteAllocationResponse> Allocations);

public sealed record WithdrawalQuoteStatusResponse(
    Guid QuoteId,
    WithdrawalQuoteStatus Status,
    DateTimeOffset ExpiresAtUtc);
