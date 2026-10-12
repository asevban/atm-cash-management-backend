using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class WithdrawalQuoteConfiguration : IEntityTypeConfiguration<WithdrawalQuote>
{
    public void Configure(EntityTypeBuilder<WithdrawalQuote> builder)
    {
        builder.ToTable("withdrawal_quotes", table =>
        {
            table.HasCheckConstraint("ck_withdrawal_quotes_card_identifier", "length(\"CardIdentifier\") = 64");
            table.HasCheckConstraint("ck_withdrawal_quotes_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_withdrawal_quotes_requested_amount", "\"RequestedAmount\" > 0");
            table.HasCheckConstraint("ck_withdrawal_quotes_limit_reservation", "\"LimitReservedAmount\" > 0");
            table.HasCheckConstraint("ck_withdrawal_quotes_status", "\"Status\" IN ('Active', 'Confirmed', 'Cancelled', 'Expired')");
            table.HasCheckConstraint("ck_withdrawal_quotes_utc_window", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
        });

        builder.HasKey(quote => quote.Id);
        builder.Property(quote => quote.CardIdentifier).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(quote => quote.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(quote => quote.LimitBusinessDate).HasColumnType("date").IsRequired();
        builder.Property(quote => quote.Status).HasConversion<string>().HasMaxLength(12).IsRequired();
        builder.Property(quote => quote.RequestedAmount).IsRequired();
        builder.Property(quote => quote.LimitReservedAmount).IsRequired();
        builder.Property(quote => quote.CreatedAtUtc).IsRequired();
        builder.Property(quote => quote.ExpiresAtUtc).IsRequired();
        builder.HasIndex(quote => new { quote.AtmId, quote.Status, quote.ExpiresAtUtc });
        builder.HasIndex(quote => new { quote.CardIdentifier, quote.LimitBusinessDate, quote.Currency });
        builder.HasOne<Atm>().WithMany().HasForeignKey(quote => quote.AtmId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(quote => quote.Options).WithOne(option => option.Quote)
            .HasForeignKey(option => option.QuoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(quote => quote.Reservations).WithOne(reservation => reservation.Quote)
            .HasForeignKey(reservation => reservation.QuoteId).OnDelete(DeleteBehavior.Restrict);
    }
}
