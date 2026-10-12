using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application;

/// <summary>
/// Pure deposit routing. Matching recycling cassettes are filled by slot number;
/// unmatched and overflow notes then use the deposit-only cassette.
/// </summary>
public sealed class DepositAllocationPlanner
{
    public DepositAllocationPlan Plan(
        IReadOnlyCollection<CassetteCapacitySnapshot> cassettes,
        IReadOnlyCollection<BanknoteLoadRequest> notes)
    {
        ArgumentNullException.ThrowIfNull(cassettes);
        ArgumentNullException.ThrowIfNull(notes);

        if (cassettes.Select(cassette => cassette.CassetteNumber).Distinct().Count() != cassettes.Count)
        {
            throw new ArgumentException("Cassette slot numbers must be unique.", nameof(cassettes));
        }

        foreach (var cassette in cassettes)
        {
            if (cassette.CassetteNumber is < 1 or > 5 || cassette.Capacity is < 1 or > 100 ||
                cassette.PhysicalCount < 0 || cassette.PhysicalCount > cassette.Capacity)
            {
                throw new ArgumentException("Cassette capacity snapshot is invalid.", nameof(cassettes));
            }
        }

        var depositCassettes = cassettes.Where(cassette => cassette.Type == CassetteType.DepositOnly).ToArray();
        if (depositCassettes.Length != 1)
        {
            throw new ArgumentException("Exactly one deposit-only cassette is required.", nameof(cassettes));
        }

        var recyclingCassettes = cassettes
            .Where(cassette => cassette.Type == CassetteType.Recycling)
            .OrderBy(cassette => cassette.CassetteNumber)
            .ToArray();
        if (recyclingCassettes.Length > 4)
        {
            throw new ArgumentException("No more than four recycling cassettes are supported.", nameof(cassettes));
        }

        if (notes.Any(note => note is null || !Enum.IsDefined(note.Currency) || !Enum.IsDefined(note.Denomination) || note.Count < 0))
        {
            throw new ArgumentException("Deposit notes must use supported currencies and denominations with non-negative counts.", nameof(notes));
        }

        if (notes.GroupBy(note => (note.Currency, note.Denomination)).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Deposit note types must be unique.", nameof(notes));
        }

        var remainingCapacity = cassettes.ToDictionary(
            cassette => cassette.CassetteNumber,
            cassette => cassette.Capacity - cassette.PhysicalCount);
        var placements = new List<DepositPlacementResponse>();
        var returns = new List<DepositCapacityReturnResponse>();
        var depositCassette = depositCassettes[0];

        foreach (var note in notes.OrderBy(note => note.Currency).ThenBy(note => note.Denomination))
        {
            var remainingNotes = note.Count;
            foreach (var cassette in recyclingCassettes.Where(cassette =>
                         cassette.Currency == note.Currency && cassette.Denomination == note.Denomination))
            {
                var accepted = Math.Min(remainingNotes, remainingCapacity[cassette.CassetteNumber]);
                if (accepted <= 0)
                {
                    continue;
                }

                placements.Add(new DepositPlacementResponse(cassette.CassetteNumber, note.Currency, note.Denomination, accepted));
                remainingCapacity[cassette.CassetteNumber] -= accepted;
                remainingNotes -= accepted;
                if (remainingNotes == 0)
                {
                    break;
                }
            }

            var depositAccepted = Math.Min(remainingNotes, remainingCapacity[depositCassette.CassetteNumber]);
            if (depositAccepted > 0)
            {
                placements.Add(new DepositPlacementResponse(
                    depositCassette.CassetteNumber,
                    note.Currency,
                    note.Denomination,
                    depositAccepted));
                remainingCapacity[depositCassette.CassetteNumber] -= depositAccepted;
                remainingNotes -= depositAccepted;
            }

            if (remainingNotes > 0)
            {
                returns.Add(new DepositCapacityReturnResponse(note.Currency, note.Denomination, remainingNotes));
            }
        }

        return new DepositAllocationPlan(placements, returns);
    }
}
