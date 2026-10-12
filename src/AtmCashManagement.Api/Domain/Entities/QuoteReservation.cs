using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class QuoteReservation
{
    private QuoteReservation()
    {
    }

    public QuoteReservation(Guid quoteId, Guid cassetteId, Currency currency, BanknoteDenomination denomination, int reservedCount)
    {
        if (quoteId == Guid.Empty || cassetteId == Guid.Empty || !Enum.IsDefined(currency) ||
            !Enum.IsDefined(denomination) || reservedCount <= 0)
        {
            throw new ArgumentException("A quote reservation must identify a cassette and a positive supported note count.");
        }

        Id = Guid.NewGuid();
        QuoteId = quoteId;
        CassetteId = cassetteId;
        Currency = currency;
        Denomination = denomination;
        ReservedCount = reservedCount;
    }

    public Guid Id { get; private set; }
    public Guid QuoteId { get; private set; }
    public Guid CassetteId { get; private set; }
    public Currency Currency { get; private set; }
    public BanknoteDenomination Denomination { get; private set; }
    public int ReservedCount { get; private set; }
    public WithdrawalQuote Quote { get; private set; } = null!;
    public Cassette Cassette { get; private set; } = null!;
}
