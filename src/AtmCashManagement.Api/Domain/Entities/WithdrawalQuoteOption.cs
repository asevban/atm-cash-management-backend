using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class WithdrawalQuoteOption
{
    private WithdrawalQuoteOption()
    {
    }

    public WithdrawalQuoteOption(Guid quoteId, WithdrawalOptionKind kind, Currency currency, int amount, int noteCount)
    {
        if (quoteId == Guid.Empty || !Enum.IsDefined(kind) || !Enum.IsDefined(currency) || amount <= 0 || noteCount <= 0)
        {
            throw new ArgumentException("A quote option must have valid identifiers and positive payout values.");
        }

        Id = Guid.NewGuid();
        QuoteId = quoteId;
        Kind = kind;
        Currency = currency;
        Amount = amount;
        NoteCount = noteCount;
    }

    public Guid Id { get; private set; }
    public Guid QuoteId { get; private set; }
    public WithdrawalOptionKind Kind { get; private set; }
    public Currency Currency { get; private set; }
    public int Amount { get; private set; }
    public int NoteCount { get; private set; }
    public WithdrawalQuote Quote { get; private set; } = null!;
    public ICollection<WithdrawalQuoteOptionAllocation> Allocations { get; private set; } = new List<WithdrawalQuoteOptionAllocation>();

    public void AddAllocation(WithdrawalQuoteOptionAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        if (allocation.OptionId != Id || Allocations.Any(existing =>
                existing.CassetteId == allocation.CassetteId && existing.Currency == allocation.Currency &&
                existing.Denomination == allocation.Denomination))
        {
            throw new ArgumentException("The allocation must belong to this option and be unique by cassette and note type.", nameof(allocation));
        }

        Allocations.Add(allocation);
    }
}
