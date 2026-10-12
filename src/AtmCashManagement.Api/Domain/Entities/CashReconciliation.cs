using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

/// <summary>Durable physical count, valuation snapshot, and (when needed) approval state.</summary>
public sealed class CashReconciliation
{
    private CashReconciliation()
    {
    }

    public CashReconciliation(
        Guid atmId,
        DateTimeOffset occurredAt,
        string createdBy,
        decimal tryToTryRateSnapshot,
        decimal usdToTryRateSnapshot,
        decimal eurToTryRateSnapshot)
    {
        if (atmId == Guid.Empty)
        {
            throw new ArgumentException("An ATM identifier is required.", nameof(atmId));
        }

        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
        {
            throw new ArgumentException("A reconciliation operator between 1 and 100 characters is required.", nameof(createdBy));
        }

        if (tryToTryRateSnapshot != 1m || usdToTryRateSnapshot <= 0m || eurToTryRateSnapshot <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(tryToTryRateSnapshot), "Positive rate snapshots are required and TRY-to-TRY must equal 1.");
        }

        Id = Guid.NewGuid();
        AtmId = atmId;
        OccurredAt = occurredAt;
        CreatedBy = createdBy.Trim();
        TryToTryRateSnapshot = tryToTryRateSnapshot;
        UsdToTryRateSnapshot = usdToTryRateSnapshot;
        EurToTryRateSnapshot = eurToTryRateSnapshot;
        Status = CashReconciliationStatus.Completed;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public CashReconciliationStatus Status { get; private set; }
    public decimal? TotalAbsoluteDifferenceTry { get; private set; }
    public decimal? TryToTryRateSnapshot { get; private set; }
    public decimal? UsdToTryRateSnapshot { get; private set; }
    public decimal? EurToTryRateSnapshot { get; private set; }
    public ICollection<CashReconciliationLine> Lines { get; private set; } = new List<CashReconciliationLine>();

    public void SetValuation(decimal totalAbsoluteDifferenceTry)
    {
        if (totalAbsoluteDifferenceTry < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAbsoluteDifferenceTry));
        }

        TotalAbsoluteDifferenceTry = totalAbsoluteDifferenceTry;
    }

    public void RequireApproval()
    {
        if (Status != CashReconciliationStatus.Completed)
        {
            throw new InvalidOperationException("Only an unapproved reconciliation can enter approval state.");
        }

        Status = CashReconciliationStatus.AwaitingApproval;
    }

    public void Approve(string approver, DateTimeOffset approvedAt)
    {
        if (Status != CashReconciliationStatus.AwaitingApproval)
        {
            throw new InvalidOperationException("Only a reconciliation awaiting approval can be approved.");
        }

        if (string.IsNullOrWhiteSpace(approver) ||
            string.Equals(CreatedBy, approver.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The reconciliation creator cannot approve their own reconciliation.");
        }

        ApprovedBy = approver.Trim();
        ApprovedAt = approvedAt;
        Status = CashReconciliationStatus.Approved;
    }

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
