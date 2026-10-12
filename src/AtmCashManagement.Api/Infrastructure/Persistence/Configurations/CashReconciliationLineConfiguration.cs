using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CashReconciliationLineConfiguration : IEntityTypeConfiguration<CashReconciliationLine>
{
    public void Configure(EntityTypeBuilder<CashReconciliationLine> builder)
    {
        builder.ToTable("cash_reconciliation_lines", table =>
        {
            table.HasCheckConstraint("ck_cash_reconciliation_lines_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_cash_reconciliation_lines_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
            table.HasCheckConstraint("ck_cash_reconciliation_lines_expected", "\"ExpectedCount\" BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_cash_reconciliation_lines_counted", "\"CountedCount\" BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "ck_cash_reconciliation_lines_difference",
                "\"DifferenceCount\" = \"CountedCount\" - \"ExpectedCount\"");
        });

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(line => line.Denomination).IsRequired();
        builder.Property(line => line.ExpectedCount).IsRequired();
        builder.Property(line => line.CountedCount).IsRequired();
        builder.Property(line => line.DifferenceCount).IsRequired();
        builder.HasIndex(line => new { line.CashReconciliationId, line.CassetteId, line.Currency, line.Denomination }).IsUnique();
        builder.HasOne(line => line.Cassette)
            .WithMany()
            .HasForeignKey(line => line.CassetteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
