namespace AtmCashManagement.Api.Domain.Entities;

/// <summary>
/// Durable reconciliation snapshot header. Approval workflow can be added to this record later.
/// </summary>
public sealed class CashReconciliation
{
    private CashReconciliation()
    {
    }

    public CashReconciliation(Guid atmId, DateTimeOffset occurredAt)
    {
        if (atmId == Guid.Empty)
        {
            throw new ArgumentException("An ATM identifier is required.", nameof(atmId));
        }

        Id = Guid.NewGuid();
        AtmId = atmId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public ICollection<CashReconciliationLine> Lines { get; private set; } = new List<CashReconciliationLine>();

    public void AddLine(CashReconciliationLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.CashReconciliationId != Id)
        {
            throw new ArgumentException("The reconciliation line must belong to this reconciliation.", nameof(line));
        }

        Lines.Add(line);
    }
}
