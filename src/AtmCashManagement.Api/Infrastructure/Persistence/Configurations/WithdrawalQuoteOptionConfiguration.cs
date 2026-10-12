using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class WithdrawalQuoteOptionConfiguration : IEntityTypeConfiguration<WithdrawalQuoteOption>
{
    public void Configure(EntityTypeBuilder<WithdrawalQuoteOption> builder)
    {
        builder.ToTable("withdrawal_quote_options", table =>
        {
            table.HasCheckConstraint("ck_withdrawal_quote_options_kind", "\"Kind\" IN ('Exact', 'Lower', 'Upper')");
            table.HasCheckConstraint("ck_withdrawal_quote_options_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_withdrawal_quote_options_amount", "\"Amount\" > 0");
            table.HasCheckConstraint("ck_withdrawal_quote_options_note_count", "\"NoteCount\" > 0");
        });

        builder.HasKey(option => option.Id);
        builder.Property(option => option.Kind).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(option => option.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(option => option.Amount).IsRequired();
        builder.Property(option => option.NoteCount).IsRequired();
        builder.HasIndex(option => new { option.QuoteId, option.Kind }).IsUnique();
        builder.HasMany(option => option.Allocations).WithOne(allocation => allocation.Option)
            .HasForeignKey(allocation => allocation.OptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
