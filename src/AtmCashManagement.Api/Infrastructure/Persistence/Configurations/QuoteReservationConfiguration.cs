using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class QuoteReservationConfiguration : IEntityTypeConfiguration<QuoteReservation>
{
    public void Configure(EntityTypeBuilder<QuoteReservation> builder)
    {
        builder.ToTable("quote_reservations", table =>
        {
            table.HasCheckConstraint("ck_quote_reservations_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_quote_reservations_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
            table.HasCheckConstraint("ck_quote_reservations_count", "\"ReservedCount\" > 0");
        });

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(reservation => reservation.Denomination).IsRequired();
        builder.Property(reservation => reservation.ReservedCount).IsRequired();
        builder.HasIndex(reservation => new { reservation.QuoteId, reservation.CassetteId, reservation.Currency, reservation.Denomination }).IsUnique();
        builder.HasOne(reservation => reservation.Cassette).WithMany()
            .HasForeignKey(reservation => reservation.CassetteId).OnDelete(DeleteBehavior.Restrict);
    }
}
