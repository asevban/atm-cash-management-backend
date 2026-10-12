using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class WithdrawalQuote
{
    private WithdrawalQuote()
    {
    }

    public WithdrawalQuote(
        Guid atmId,
        string cardIdentifier,
        Currency currency,
        int requestedAmount,
        DateOnly limitBusinessDate,
        int limitReservedAmount,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (atmId == Guid.Empty || string.IsNullOrWhiteSpace(cardIdentifier) || cardIdentifier.Length != 64)
        {
            throw new ArgumentException("A valid ATM and card identifier are required.");
        }

        if (!Enum.IsDefined(currency) || requestedAmount <= 0 || limitReservedAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedAmount), "Currency and quote amounts must be valid and positive.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero || expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Quote timestamps must be UTC and expiry must follow creation.");
        }

        Id = Guid.NewGuid();
        AtmId = atmId;
        CardIdentifier = cardIdentifier;
        Currency = currency;
        RequestedAmount = requestedAmount;
        LimitBusinessDate = limitBusinessDate;
        LimitReservedAmount = limitReservedAmount;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Status = WithdrawalQuoteStatus.Active;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public string CardIdentifier { get; private set; } = string.Empty;
    public Currency Currency { get; private set; }
    public int RequestedAmount { get; private set; }
    public DateOnly LimitBusinessDate { get; private set; }
    public int LimitReservedAmount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public WithdrawalQuoteStatus Status { get; private set; }
    public ICollection<WithdrawalQuoteOption> Options { get; private set; } = new List<WithdrawalQuoteOption>();
    public ICollection<QuoteReservation> Reservations { get; private set; } = new List<QuoteReservation>();

    public void AddOption(WithdrawalQuoteOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.QuoteId != Id || Options.Any(existing => existing.Kind == option.Kind))
        {
            throw new ArgumentException("The option must belong to this quote and its kind must be unique.", nameof(option));
        }

        Options.Add(option);
    }

    public void AddReservation(QuoteReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.QuoteId != Id || Reservations.Any(existing =>
                existing.CassetteId == reservation.CassetteId &&
                existing.Currency == reservation.Currency &&
                existing.Denomination == reservation.Denomination))
        {
            throw new ArgumentException("The reservation must belong to this quote and be unique by cassette and note type.", nameof(reservation));
        }

        Reservations.Add(reservation);
    }

    public void Confirm(DateTimeOffset occurredAtUtc) => Close(WithdrawalQuoteStatus.Confirmed, occurredAtUtc);
    public void Cancel(DateTimeOffset occurredAtUtc) => Close(WithdrawalQuoteStatus.Cancelled, occurredAtUtc);
    public void Expire(DateTimeOffset occurredAtUtc) => Close(WithdrawalQuoteStatus.Expired, occurredAtUtc);

    private void Close(WithdrawalQuoteStatus status, DateTimeOffset occurredAtUtc)
    {
        if (Status != WithdrawalQuoteStatus.Active || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("Only an active quote can be closed with a UTC timestamp.");
        }

        Status = status;
        ClosedAtUtc = occurredAtUtc;
    }
}
