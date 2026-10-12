using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CashReconciliationConfiguration : IEntityTypeConfiguration<CashReconciliation>
{
    public void Configure(EntityTypeBuilder<CashReconciliation> builder)
    {
        builder.ToTable("cash_reconciliations", table =>
        {
            table.HasCheckConstraint(
                "ck_cash_reconciliations_status_approval",
                "((\"Status\" = 'Approved' AND \"ApprovedBy\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL AND lower(\"ApprovedBy\") <> lower(\"CreatedBy\")) OR (\"Status\" <> 'Approved' AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL))");
            table.HasCheckConstraint(
                "ck_cash_reconciliations_valuation",
                "(\"TotalAbsoluteDifferenceTry\" IS NULL OR (\"TotalAbsoluteDifferenceTry\" >= 0 AND \"TryToTryRateSnapshot\" IS NOT NULL AND \"TryToTryRateSnapshot\" = 1 AND \"UsdToTryRateSnapshot\" IS NOT NULL AND \"UsdToTryRateSnapshot\" > 0 AND \"EurToTryRateSnapshot\" IS NOT NULL AND \"EurToTryRateSnapshot\" > 0))");
            table.HasCheckConstraint(
                "ck_cash_reconciliations_threshold_status",
                "((\"Status\" = 'Completed' AND (\"TotalAbsoluteDifferenceTry\" IS NULL OR \"TotalAbsoluteDifferenceTry\" <= 1000)) OR (\"Status\" IN ('AwaitingApproval', 'Approved') AND \"TotalAbsoluteDifferenceTry\" IS NOT NULL AND \"TotalAbsoluteDifferenceTry\" > 1000))");
        });
        builder.HasKey(reconciliation => reconciliation.Id);
        builder.Property(reconciliation => reconciliation.OccurredAt).IsRequired();
        builder.Property(reconciliation => reconciliation.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(reconciliation => reconciliation.ApprovedBy).HasMaxLength(100);
        builder.Property(reconciliation => reconciliation.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(reconciliation => reconciliation.TotalAbsoluteDifferenceTry).HasPrecision(18, 6);
        builder.Property(reconciliation => reconciliation.TryToTryRateSnapshot).HasPrecision(18, 6);
        builder.Property(reconciliation => reconciliation.UsdToTryRateSnapshot).HasPrecision(18, 6);
        builder.Property(reconciliation => reconciliation.EurToTryRateSnapshot).HasPrecision(18, 6);
        builder.HasIndex(reconciliation => new { reconciliation.AtmId, reconciliation.OccurredAt });
        builder.HasIndex(reconciliation => reconciliation.AtmId)
            .IsUnique()
            .HasFilter("\"Status\" = 'AwaitingApproval'")
            .HasDatabaseName("IX_cash_reconciliations_pending_approval");
        builder.HasOne<Atm>()
            .WithMany()
            .HasForeignKey(reconciliation => reconciliation.AtmId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(reconciliation => reconciliation.Lines)
            .WithOne(line => line.CashReconciliation)
            .HasForeignKey(line => line.CashReconciliationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
