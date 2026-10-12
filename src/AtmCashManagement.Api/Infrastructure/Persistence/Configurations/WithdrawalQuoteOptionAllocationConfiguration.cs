using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class WithdrawalQuoteOptionAllocationConfiguration : IEntityTypeConfiguration<WithdrawalQuoteOptionAllocation>
{
    public void Configure(EntityTypeBuilder<WithdrawalQuoteOptionAllocation> builder)
    {
        builder.ToTable("withdrawal_quote_option_allocations", table =>
        {
            table.HasCheckConstraint("ck_withdrawal_quote_option_allocations_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_withdrawal_quote_option_allocations_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
            table.HasCheckConstraint("ck_withdrawal_quote_option_allocations_count", "\"Count\" > 0");
        });

        builder.HasKey(allocation => allocation.Id);
        builder.Property(allocation => allocation.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(allocation => allocation.Denomination).IsRequired();
        builder.Property(allocation => allocation.Count).IsRequired();
        builder.HasIndex(allocation => new { allocation.OptionId, allocation.CassetteId, allocation.Currency, allocation.Denomination }).IsUnique();
        builder.HasOne(allocation => allocation.Cassette).WithMany()
            .HasForeignKey(allocation => allocation.CassetteId).OnDelete(DeleteBehavior.Restrict);
    }
}
