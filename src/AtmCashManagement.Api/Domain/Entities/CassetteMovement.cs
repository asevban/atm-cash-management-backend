using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class CassetteMovement
{
    private CassetteMovement()
    {
    }

    public CassetteMovement(
        Guid atmId,
        Guid cassetteId,
        CassetteMovementType type,
        Currency currency,
        BanknoteDenomination denomination,
        int physicalCountDelta,
        int reservedCountDelta,
        DateTimeOffset occurredAt,
        string transactionReference,
        bool isInitialLoad = false)
    {
        if (atmId == Guid.Empty || cassetteId == Guid.Empty)
        {
            throw new ArgumentException("ATM and cassette identifiers are required.");
        }

        if (!Enum.IsDefined(type) || !Enum.IsDefined(currency) || !Enum.IsDefined(denomination))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Movement type, currency and denomination must be supported values.");
        }

        if (physicalCountDelta == 0 && reservedCountDelta == 0)
        {
            throw new ArgumentException("A movement must change physical or reserved note counts.");
        }

        if (string.IsNullOrWhiteSpace(transactionReference) || transactionReference.Length > 100)
        {
            throw new ArgumentException("A transaction reference between 1 and 100 characters is required.", nameof(transactionReference));
        }

        if (isInitialLoad && type != CassetteMovementType.Load)
        {
            throw new ArgumentException("Only LOAD movements can be marked as an initial load.", nameof(isInitialLoad));
        }

        Id = Guid.NewGuid();
        AtmId = atmId;
        CassetteId = cassetteId;
        Type = type;
        Currency = currency;
        Denomination = denomination;
        PhysicalCountDelta = physicalCountDelta;
        ReservedCountDelta = reservedCountDelta;
        OccurredAt = occurredAt;
        TransactionReference = transactionReference;
        IsInitialLoad = isInitialLoad;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public Guid CassetteId { get; private set; }
    public CassetteMovementType Type { get; private set; }
    public Currency Currency { get; private set; }
    public BanknoteDenomination Denomination { get; private set; }
    public int PhysicalCountDelta { get; private set; }
    public int ReservedCountDelta { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string TransactionReference { get; private set; } = string.Empty;
    public bool IsInitialLoad { get; private set; }
    public Atm Atm { get; private set; } = null!;
    public Cassette Cassette { get; private set; } = null!;
}
