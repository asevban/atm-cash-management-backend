using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CashWithdrawalConfiguration : IEntityTypeConfiguration<CashWithdrawal>
{
    public void Configure(EntityTypeBuilder<CashWithdrawal> builder)
    {
        builder.ToTable("cash_withdrawals", table =>
        {
            table.HasCheckConstraint("ck_cash_withdrawals_identifier", "length(\"CardIdentifier\") = 64");
            table.HasCheckConstraint("ck_cash_withdrawals_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_cash_withdrawals_amount", "\"Amount\" > 0");
        });

        builder.HasKey(withdrawal => withdrawal.Id);
        builder.Property(withdrawal => withdrawal.CardIdentifier).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(withdrawal => withdrawal.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(withdrawal => withdrawal.Amount).IsRequired();
        builder.Property(withdrawal => withdrawal.OccurredAtUtc).IsRequired();
        builder.HasIndex(withdrawal => withdrawal.QuoteId).IsUnique();
        builder.HasOne(withdrawal => withdrawal.Atm).WithMany().HasForeignKey(withdrawal => withdrawal.AtmId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(withdrawal => withdrawal.Quote).WithMany().HasForeignKey(withdrawal => withdrawal.QuoteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(withdrawal => withdrawal.Option).WithMany().HasForeignKey(withdrawal => withdrawal.OptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
