using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class CashWithdrawal
{
    private CashWithdrawal()
    {
    }

    public CashWithdrawal(Guid id, Guid atmId, Guid quoteId, Guid optionId, string cardIdentifier,
        Currency currency, int amount, DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty || atmId == Guid.Empty || quoteId == Guid.Empty || optionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(cardIdentifier) || cardIdentifier.Length != 64 ||
            !Enum.IsDefined(currency) || amount <= 0 || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Withdrawal identifiers, card identifier, currency, amount and UTC timestamp are required.");
        }

        Id = id;
        AtmId = atmId;
        QuoteId = quoteId;
        OptionId = optionId;
        CardIdentifier = cardIdentifier;
        Currency = currency;
        Amount = amount;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public Guid QuoteId { get; private set; }
    public Guid OptionId { get; private set; }
    public string CardIdentifier { get; private set; } = string.Empty;
    public Currency Currency { get; private set; }
    public int Amount { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Atm Atm { get; private set; } = null!;
    public WithdrawalQuote Quote { get; private set; } = null!;
    public WithdrawalQuoteOption Option { get; private set; } = null!;
}
