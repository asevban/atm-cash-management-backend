using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

/// <summary>
/// A snapshot of one cassette's configured note stock, supplied by the caller.
/// Counts are inputs only; note selection never changes them.
/// </summary>
public sealed record CassetteStockInput(
    int CassetteNumber,
    CassetteType Type,
    int Capacity,
    Currency? Currency,
    BanknoteDenomination? Denomination,
    int PhysicalCount,
    int ReservedCount);

public enum NoteSelectionOutcome
{
    Exact,
    Alternatives,
    NoAvailableCash,
    NoPermittedPayout
}

public sealed record CassetteNoteAllocation(
    int CassetteNumber,
    Currency Currency,
    BanknoteDenomination Denomination,
    int Count);

public sealed record NoteSelectionOption(
    int Amount,
    int NoteCount,
    IReadOnlyList<CassetteNoteAllocation> Allocations);

public sealed record NoteSelectionResult(
    NoteSelectionOutcome Outcome,
    NoteSelectionOption? Exact,
    NoteSelectionOption? Lower,
    NoteSelectionOption? Upper);
