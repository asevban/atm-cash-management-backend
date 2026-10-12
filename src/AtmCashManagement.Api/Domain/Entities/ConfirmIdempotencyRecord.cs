namespace AtmCashManagement.Api.Domain.Entities;

public sealed class ConfirmIdempotencyRecord
{
    private ConfirmIdempotencyRecord()
    {
    }

    public ConfirmIdempotencyRecord(string key, Guid quoteId, Guid selectedOptionId, Guid withdrawalId, DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200 || quoteId == Guid.Empty ||
            selectedOptionId == Guid.Empty || withdrawalId == Guid.Empty || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A bounded idempotency key and valid transaction identifiers are required.");
        }

        Id = Guid.NewGuid();
        Key = key;
        QuoteId = quoteId;
        SelectedOptionId = selectedOptionId;
        WithdrawalId = withdrawalId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public Guid QuoteId { get; private set; }
    public Guid SelectedOptionId { get; private set; }
    public Guid WithdrawalId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public CashWithdrawal Withdrawal { get; private set; } = null!;
}
