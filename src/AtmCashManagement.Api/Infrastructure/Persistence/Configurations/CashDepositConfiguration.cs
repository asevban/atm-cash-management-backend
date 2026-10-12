using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CashDepositConfiguration : IEntityTypeConfiguration<CashDeposit>
{
    public void Configure(EntityTypeBuilder<CashDeposit> builder)
    {
        builder.ToTable("cash_deposits", table =>
            table.HasCheckConstraint("ck_cash_deposits_unrecognized_count", "\"UnrecognizedNoteCount\" >= 0"));

        builder.HasKey(deposit => deposit.Id);
        builder.Property(deposit => deposit.OccurredAt).IsRequired();
        builder.Property(deposit => deposit.UnrecognizedNoteCount).IsRequired();
        builder.HasIndex(deposit => new { deposit.AtmId, deposit.OccurredAt });
        builder.HasOne<Atm>()
            .WithMany()
            .HasForeignKey(deposit => deposit.AtmId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(deposit => deposit.Notes)
            .WithOne(note => note.CashDeposit)
            .HasForeignKey(note => note.CashDepositId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
