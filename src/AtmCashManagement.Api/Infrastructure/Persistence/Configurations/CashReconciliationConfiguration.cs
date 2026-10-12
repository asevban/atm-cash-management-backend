using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CashReconciliationConfiguration : IEntityTypeConfiguration<CashReconciliation>
{
    public void Configure(EntityTypeBuilder<CashReconciliation> builder)
    {
        builder.ToTable("cash_reconciliations");
        builder.HasKey(reconciliation => reconciliation.Id);
        builder.Property(reconciliation => reconciliation.OccurredAt).IsRequired();
        builder.HasIndex(reconciliation => new { reconciliation.AtmId, reconciliation.OccurredAt });
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
