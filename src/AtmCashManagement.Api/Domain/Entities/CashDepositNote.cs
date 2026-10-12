using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class CashDepositNote
{
    private CashDepositNote()
    {
    }

    public CashDepositNote(
        Guid cashDepositId,
        Currency currency,
        BanknoteDenomination denomination,
        int insertedCount,
        int acceptedCount,
        int returnedCount)
    {
        if (cashDepositId == Guid.Empty)
        {
            throw new ArgumentException("A deposit identifier is required.", nameof(cashDepositId));
        }

        if (!Enum.IsDefined(currency) || !Enum.IsDefined(denomination))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), "Currency and denomination must be supported values.");
        }

        if (insertedCount <= 0 || acceptedCount < 0 || returnedCount < 0 ||
            (long)acceptedCount + returnedCount != insertedCount)
        {
            throw new ArgumentOutOfRangeException(nameof(insertedCount), "Inserted notes must equal accepted plus returned notes.");
        }

        Id = Guid.NewGuid();
        CashDepositId = cashDepositId;
        Currency = currency;
        Denomination = denomination;
        InsertedCount = insertedCount;
        AcceptedCount = acceptedCount;
        ReturnedCount = returnedCount;
    }

    public Guid Id { get; private set; }
    public Guid CashDepositId { get; private set; }
    public Currency Currency { get; private set; }
    public BanknoteDenomination Denomination { get; private set; }
    public int InsertedCount { get; private set; }
    public int AcceptedCount { get; private set; }
    public int ReturnedCount { get; private set; }
    public CashDeposit CashDeposit { get; private set; } = null!;
}
