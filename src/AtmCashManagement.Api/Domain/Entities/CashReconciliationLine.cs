using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class CashReconciliationLine
{
    private CashReconciliationLine()
    {
    }

    public CashReconciliationLine(
        Guid cashReconciliationId,
        Guid cassetteId,
        Currency currency,
        BanknoteDenomination denomination,
        int expectedCount,
        int countedCount)
    {
        if (cashReconciliationId == Guid.Empty || cassetteId == Guid.Empty)
        {
            throw new ArgumentException("Reconciliation and cassette identifiers are required.");
        }

        if (!Enum.IsDefined(currency) || !Enum.IsDefined(denomination))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), "Currency and denomination must be supported values.");
        }

        if (expectedCount is < 0 or > 100 || countedCount is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(countedCount), "Note counts must be between 0 and 100.");
        }

        Id = Guid.NewGuid();
        CashReconciliationId = cashReconciliationId;
        CassetteId = cassetteId;
        Currency = currency;
        Denomination = denomination;
        ExpectedCount = expectedCount;
        CountedCount = countedCount;
        DifferenceCount = countedCount - expectedCount;
    }

    public Guid Id { get; private set; }
    public Guid CashReconciliationId { get; private set; }
    public Guid CassetteId { get; private set; }
    public Currency Currency { get; private set; }
    public BanknoteDenomination Denomination { get; private set; }
    public int ExpectedCount { get; private set; }
    public int CountedCount { get; private set; }
    public int DifferenceCount { get; private set; }
    public CashReconciliation CashReconciliation { get; private set; } = null!;
    public Cassette Cassette { get; private set; } = null!;
}
