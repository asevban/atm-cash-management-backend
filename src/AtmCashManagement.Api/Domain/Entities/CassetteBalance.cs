using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class CassetteBalance
{
    private CassetteBalance()
    {
    }

    public CassetteBalance(
        Guid cassetteId,
        Currency currency,
        BanknoteDenomination denomination,
        int physicalCount = 0,
        int reservedCount = 0)
    {
        if (cassetteId == Guid.Empty)
        {
            throw new ArgumentException("A cassette identifier is required.", nameof(cassetteId));
        }

        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), "Unsupported currency.");
        }

        if (!Enum.IsDefined(denomination))
        {
            throw new ArgumentOutOfRangeException(nameof(denomination), "Unsupported banknote denomination.");
        }

        if (physicalCount is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalCount), "Physical note count must be between 0 and 100.");
        }

        if (reservedCount < 0 || reservedCount > physicalCount)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedCount), "Reserved note count must be between 0 and the physical count.");
        }

        Id = Guid.NewGuid();
        CassetteId = cassetteId;
        Currency = currency;
        Denomination = denomination;
        PhysicalCount = physicalCount;
        ReservedCount = reservedCount;
    }

    public Guid Id { get; private set; }
    public Guid CassetteId { get; private set; }
    public Currency Currency { get; private set; }
    public BanknoteDenomination Denomination { get; private set; }
    public int PhysicalCount { get; private set; }
    public int ReservedCount { get; private set; }
    public int AvailableCount => PhysicalCount - ReservedCount;
    public Cassette Cassette { get; private set; } = null!;

    public void AddPhysicalNotes(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The number of notes to load must be positive.");
        }

        if (PhysicalCount + count > 100)
        {
            throw new InvalidOperationException("A balance entry cannot exceed 100 physical notes.");
        }

        PhysicalCount += count;
    }

    public void ReconcilePhysicalCount(int countedCount)
    {
        if (countedCount is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(countedCount), "Physical note count must be between 0 and 100.");
        }

        if (ReservedCount > countedCount)
        {
            throw new InvalidOperationException("Physical count cannot be reconciled below the reserved note count.");
        }

        PhysicalCount = countedCount;
    }

    public void ReserveNotes(int count)
    {
        if (count <= 0 || (long)ReservedCount + count > PhysicalCount)
        {
            throw new InvalidOperationException("The requested notes are not available for reservation.");
        }

        ReservedCount += count;
    }

    public void ReleaseReservedNotes(int count)
    {
        if (count <= 0 || count > ReservedCount)
        {
            throw new InvalidOperationException("The requested reservation cannot be released.");
        }

        ReservedCount -= count;
    }

    public void ConsumeReservedNotes(int count)
    {
        if (count <= 0 || count > ReservedCount || count > PhysicalCount)
        {
            throw new InvalidOperationException("The requested reserved notes cannot be consumed.");
        }

        PhysicalCount -= count;
        ReservedCount -= count;
    }
}
