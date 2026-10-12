using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CashDepositNoteConfiguration : IEntityTypeConfiguration<CashDepositNote>
{
    public void Configure(EntityTypeBuilder<CashDepositNote> builder)
    {
        builder.ToTable("cash_deposit_notes", table =>
        {
            table.HasCheckConstraint("ck_cash_deposit_notes_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_cash_deposit_notes_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
            table.HasCheckConstraint("ck_cash_deposit_notes_inserted_count", "\"InsertedCount\" > 0");
            table.HasCheckConstraint(
                "ck_cash_deposit_notes_counts",
                "\"AcceptedCount\" >= 0 AND \"ReturnedCount\" >= 0 AND \"InsertedCount\" = \"AcceptedCount\" + \"ReturnedCount\"");
        });

        builder.HasKey(note => note.Id);
        builder.Property(note => note.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(note => note.Denomination).IsRequired();
        builder.Property(note => note.InsertedCount).IsRequired();
        builder.Property(note => note.AcceptedCount).IsRequired();
        builder.Property(note => note.ReturnedCount).IsRequired();
        builder.HasIndex(note => new { note.CashDepositId, note.Currency, note.Denomination }).IsUnique();
    }
}
