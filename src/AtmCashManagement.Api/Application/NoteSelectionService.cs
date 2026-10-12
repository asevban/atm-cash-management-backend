using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application;

/// <summary>
/// Finds minimum-note payouts over bounded cassette stock. The service is pure:
/// it uses the supplied stock snapshot and does not reserve or mutate cash.
/// </summary>
public sealed class NoteSelectionService : INoteSelectionService
{
    private const int MaximumCassetteCount = 5;
    private const int MaximumRecyclingCassetteCount = 4;
    private const int MaximumCassetteCapacity = 100;
    private const int Unreachable = int.MaxValue;

    public NoteSelectionResult Select(
        Currency currency,
        int requestedAmount,
        IReadOnlyCollection<CassetteStockInput> cassetteStocks,
        int? remainingDailyLimit = null)
    {
        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), "Unsupported currency.");
        }

        if (requestedAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedAmount), "The requested amount must be positive.");
        }

        if (remainingDailyLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingDailyLimit), "The remaining daily limit cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(cassetteStocks);
        ValidateCassettes(cassetteStocks);

        var matchingStocks = cassetteStocks
            .Where(stock => stock.Type == CassetteType.Recycling && stock.Currency == currency)
            .Where(stock => stock.Denomination.HasValue && stock.AvailableCount() > 0)
            .ToArray();
        if (matchingStocks.Length == 0)
        {
            return new NoteSelectionResult(NoteSelectionOutcome.NoAvailableCash, null, null, null);
        }

        var countsByDenomination = matchingStocks
            .GroupBy(stock => stock.Denomination!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(stock => stock.AvailableCount()));
        var maximumAmount = matchingStocks.Sum(stock => stock.AvailableCount() * (int)stock.Denomination!.Value);
        var (minimumNotesByAmount, steps) = BuildBoundedDynamicProgram(maximumAmount, countsByDenomination);

        if (requestedAmount <= maximumAmount && minimumNotesByAmount[requestedAmount] != Unreachable)
        {
            var exact = BuildOption(currency, requestedAmount, minimumNotesByAmount[requestedAmount], steps, matchingStocks);
            return new NoteSelectionResult(NoteSelectionOutcome.Exact, exact, null, null);
        }

        var lowerAmount = Math.Min(requestedAmount - 1, maximumAmount);
        while (lowerAmount > 0 && minimumNotesByAmount[lowerAmount] == Unreachable)
        {
            lowerAmount--;
        }

        var upperAmount = requestedAmount >= maximumAmount ? maximumAmount + 1 : requestedAmount + 1;
        while (upperAmount <= maximumAmount && minimumNotesByAmount[upperAmount] == Unreachable)
        {
            upperAmount++;
        }

        NoteSelectionOption? lower = lowerAmount > 0
            ? BuildOption(currency, lowerAmount, minimumNotesByAmount[lowerAmount], steps, matchingStocks)
            : null;
        NoteSelectionOption? upper = upperAmount <= maximumAmount &&
                                     (remainingDailyLimit is null || upperAmount <= remainingDailyLimit.Value)
            ? BuildOption(currency, upperAmount, minimumNotesByAmount[upperAmount], steps, matchingStocks)
            : null;

        var outcome = lower is null && upper is null
            ? NoteSelectionOutcome.NoPermittedPayout
            : NoteSelectionOutcome.Alternatives;
        return new NoteSelectionResult(outcome, null, lower, upper);
    }

    private static void ValidateCassettes(IReadOnlyCollection<CassetteStockInput> cassetteStocks)
    {
        if (cassetteStocks.Count > MaximumCassetteCount)
        {
            throw new ArgumentException($"An ATM cannot have more than {MaximumCassetteCount} cassettes.", nameof(cassetteStocks));
        }

        if (cassetteStocks.Any(stock => stock is null))
        {
            throw new ArgumentException("Cassette stock entries cannot be null.", nameof(cassetteStocks));
        }

        if (cassetteStocks.Select(stock => stock.CassetteNumber).Distinct().Count() != cassetteStocks.Count)
        {
            throw new ArgumentException("Cassette slot numbers must be unique.", nameof(cassetteStocks));
        }

        var recyclingCount = 0;
        foreach (var stock in cassetteStocks)
        {
            if (stock.CassetteNumber is < 1 or > MaximumCassetteCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cassetteStocks), "Cassette number must be between 1 and 5.");
            }

            if (!Enum.IsDefined(stock.Type))
            {
                throw new ArgumentOutOfRangeException(nameof(cassetteStocks), "Unsupported cassette type.");
            }

            if (stock.Capacity is < 1 or > MaximumCassetteCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(cassetteStocks), "Cassette capacity must be between 1 and 100 notes.");
            }

            if (stock.PhysicalCount < 0 || stock.PhysicalCount > stock.Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(cassetteStocks), "Physical notes must be within the cassette capacity.");
            }

            if (stock.ReservedCount < 0 || stock.ReservedCount > stock.PhysicalCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cassetteStocks), "Reserved notes must be between zero and the physical count.");
            }

            if (stock.Type == CassetteType.DepositOnly)
            {
                if (stock.Currency is not null || stock.Denomination is not null)
                {
                    throw new ArgumentException("A deposit-only cassette cannot be configured for one currency or denomination.", nameof(cassetteStocks));
                }

                continue;
            }

            recyclingCount++;
            if (stock.Currency is null || !Enum.IsDefined(stock.Currency.Value) ||
                stock.Denomination is null || !Enum.IsDefined(stock.Denomination.Value))
            {
                throw new ArgumentException("A recycling cassette requires a supported currency and denomination.", nameof(cassetteStocks));
            }
        }

        if (recyclingCount > MaximumRecyclingCassetteCount)
        {
            throw new ArgumentException($"An ATM cannot have more than {MaximumRecyclingCassetteCount} recycling cassettes.", nameof(cassetteStocks));
        }
    }

    private static (int[] MinimumNotesByAmount, IReadOnlyList<DenominationStep> Steps) BuildBoundedDynamicProgram(
        int maximumAmount,
        IReadOnlyDictionary<BanknoteDenomination, int> countsByDenomination)
    {
        var minimumNotes = new int[maximumAmount + 1];
        Array.Fill(minimumNotes, Unreachable);
        minimumNotes[0] = 0;
        var steps = new List<DenominationStep>();

        foreach (var (denomination, availableCount) in countsByDenomination.OrderByDescending(pair => (int)pair.Key))
        {
            var value = (int)denomination;
            var nextMinimumNotes = new int[maximumAmount + 1];
            Array.Fill(nextMinimumNotes, Unreachable);
            var selectedCountByAmount = new int[maximumAmount + 1];
            Array.Fill(selectedCountByAmount, -1);
            var candidateIndices = new int[(maximumAmount / value) + 2];

            for (var residue = 0; residue <= Math.Min(value - 1, maximumAmount); residue++)
            {
                var head = 0;
                var tail = 0;
                var amount = residue;
                for (var index = 0; amount <= maximumAmount; index++, amount += value)
                {
                    if (minimumNotes[amount] != Unreachable)
                    {
                        var candidateKey = minimumNotes[amount] - index;
                        while (tail > head)
                        {
                            var previousIndex = candidateIndices[tail - 1];
                            var previousAmount = residue + previousIndex * value;
                            var previousKey = minimumNotes[previousAmount] - previousIndex;
                            // Prefer fewer notes from this (lower, later-processed) denomination on ties.
                            if (previousKey < candidateKey)
                            {
                                break;
                            }

                            tail--;
                        }

                        candidateIndices[tail++] = index;
                    }

                    var firstAllowedIndex = index - availableCount;
                    while (head < tail && candidateIndices[head] < firstAllowedIndex)
                    {
                        head++;
                    }

                    if (head == tail)
                    {
                        continue;
                    }

                    var sourceIndex = candidateIndices[head];
                    var sourceAmount = residue + sourceIndex * value;
                    nextMinimumNotes[amount] = index + minimumNotes[sourceAmount] - sourceIndex;
                    selectedCountByAmount[amount] = index - sourceIndex;
                }
            }

            minimumNotes = nextMinimumNotes;
            steps.Add(new DenominationStep(denomination, selectedCountByAmount));
        }

        return (minimumNotes, steps);
    }

    private static NoteSelectionOption BuildOption(
        Currency currency,
        int amount,
        int noteCount,
        IReadOnlyList<DenominationStep> steps,
        IReadOnlyCollection<CassetteStockInput> matchingStocks)
    {
        var countsByDenomination = new Dictionary<BanknoteDenomination, int>();
        var remainingAmount = amount;
        foreach (var step in steps.Reverse())
        {
            var count = step.CountByAmount[remainingAmount];
            if (count < 0)
            {
                throw new InvalidOperationException("The selected payout could not be reconstructed from bounded stock.");
            }

            countsByDenomination[step.Denomination] = count;
            remainingAmount -= count * (int)step.Denomination;
        }

        if (remainingAmount != 0)
        {
            throw new InvalidOperationException("The selected payout reconstruction did not reach zero.");
        }

        var allocations = new List<CassetteNoteAllocation>();
        foreach (var (denomination, selectedCount) in countsByDenomination.OrderByDescending(pair => (int)pair.Key))
        {
            var remainingCount = selectedCount;
            foreach (var stock in matchingStocks
                         .Where(stock => stock.Denomination == denomination)
                         .OrderByDescending(stock => stock.PhysicalCount / (double)stock.Capacity)
                         .ThenBy(stock => stock.CassetteNumber))
            {
                var allocatedCount = Math.Min(remainingCount, stock.AvailableCount());
                if (allocatedCount > 0)
                {
                    allocations.Add(new CassetteNoteAllocation(stock.CassetteNumber, currency, denomination, allocatedCount));
                    remainingCount -= allocatedCount;
                }

                if (remainingCount == 0)
                {
                    break;
                }
            }

            if (remainingCount != 0)
            {
                throw new InvalidOperationException("The selected payout exceeds the cassette stock snapshot.");
            }
        }

        return new NoteSelectionOption(amount, noteCount, allocations);
    }

    private sealed record DenominationStep(BanknoteDenomination Denomination, int[] CountByAmount);
}

internal static class CassetteStockInputExtensions
{
    public static int AvailableCount(this CassetteStockInput stock) => stock.PhysicalCount - stock.ReservedCount;
}
