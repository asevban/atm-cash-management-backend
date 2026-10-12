using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class WithdrawalQuoteOptionAllocation
{
    private WithdrawalQuoteOptionAllocation()
    {
    }

    public WithdrawalQuoteOptionAllocation(Guid optionId, Guid cassetteId, Currency currency, BanknoteDenomination denomination, int count)
    {
        if (optionId == Guid.Empty || cassetteId == Guid.Empty || !Enum.IsDefined(currency) ||
            !Enum.IsDefined(denomination) || count <= 0)
        {
            throw new ArgumentException("A quote allocation must identify a cassette and a positive supported note count.");
        }

        Id = Guid.NewGuid();
        OptionId = optionId;
        CassetteId = cassetteId;
        Currency = currency;
        Denomination = denomination;
        Count = count;
    }

    public Guid Id { get; private set; }
    public Guid OptionId { get; private set; }
    public Guid CassetteId { get; private set; }
    public Currency Currency { get; private set; }
    public BanknoteDenomination Denomination { get; private set; }
    public int Count { get; private set; }
    public WithdrawalQuoteOption Option { get; private set; } = null!;
    public Cassette Cassette { get; private set; } = null!;
}
