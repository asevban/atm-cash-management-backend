using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CassetteMovementConfiguration : IEntityTypeConfiguration<CassetteMovement>
{
    public void Configure(EntityTypeBuilder<CassetteMovement> builder)
    {
        builder.ToTable("cassette_movements", table =>
        {
            table.HasCheckConstraint(
                "ck_cassette_movements_type",
                "\"Type\" IN ('Load', 'Unload', 'Withdrawal', 'Deposit', 'Reservation', 'ReservationRelease', 'ReconciliationAdjustment')");
            table.HasCheckConstraint("ck_cassette_movements_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_cassette_movements_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
            table.HasCheckConstraint(
                "ck_cassette_movements_delta",
                "\"PhysicalCountDelta\" <> 0 OR \"ReservedCountDelta\" <> 0");
            table.HasCheckConstraint(
                "ck_cassette_movements_initial_load_type",
                "NOT \"IsInitialLoad\" OR \"Type\" = 'Load'");
        });

        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(movement => movement.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(movement => movement.Denomination).IsRequired();
        builder.Property(movement => movement.PhysicalCountDelta).IsRequired();
        builder.Property(movement => movement.ReservedCountDelta).IsRequired();
        builder.Property(movement => movement.OccurredAt).IsRequired();
        builder.Property(movement => movement.TransactionReference).HasMaxLength(100).IsRequired();
        builder.Property(movement => movement.IsInitialLoad).IsRequired();
        builder.HasIndex(movement => new { movement.AtmId, movement.OccurredAt });
        builder.HasIndex(movement => movement.TransactionReference);
        builder.HasOne(movement => movement.Atm)
            .WithMany()
            .HasForeignKey(movement => movement.AtmId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(movement => movement.Cassette)
            .WithMany()
            .HasForeignKey(movement => movement.CassetteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
