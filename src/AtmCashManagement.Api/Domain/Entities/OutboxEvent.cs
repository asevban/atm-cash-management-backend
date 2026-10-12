using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class OutboxEvent
{
    private OutboxEvent()
    {
    }

    public OutboxEvent(
        string eventType,
        Guid atmId,
        Guid cassetteId,
        string eventKey,
        string payload,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 60)
        {
            throw new ArgumentException("An event type of at most 60 characters is required.", nameof(eventType));
        }

        if (atmId == Guid.Empty || cassetteId == Guid.Empty)
        {
            throw new ArgumentException("ATM and cassette identifiers are required.");
        }

        if (string.IsNullOrWhiteSpace(eventKey) || eventKey.Length > 200)
        {
            throw new ArgumentException("An event key of at most 200 characters is required.", nameof(eventKey));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("An event payload is required.", nameof(payload));
        }

        Id = Guid.NewGuid();
        EventType = eventType;
        AtmId = atmId;
        CassetteId = cassetteId;
        EventKey = eventKey;
        Payload = payload;
        Status = OutboxEventStatus.Pending;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid AtmId { get; private set; }
    public Guid CassetteId { get; private set; }
    public string EventKey { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public OutboxEventStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }
    public int RetryCount { get; private set; }
    public Atm Atm { get; private set; } = null!;
    public Cassette Cassette { get; private set; } = null!;

    public void MarkSent(DateTimeOffset sentAtUtc)
    {
        if (Status == OutboxEventStatus.Sent)
        {
            return;
        }

        Status = OutboxEventStatus.Sent;
        SentAtUtc = sentAtUtc.ToUniversalTime();
    }

    public void RecordFailure() => RetryCount++;
}
