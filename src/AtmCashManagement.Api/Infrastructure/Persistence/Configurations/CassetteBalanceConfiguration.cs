using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CassetteBalanceConfiguration : IEntityTypeConfiguration<CassetteBalance>
{
    public void Configure(EntityTypeBuilder<CassetteBalance> builder)
    {
        builder.ToTable("cassette_balances", table =>
        {
            table.HasCheckConstraint("ck_cassette_balances_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_cassette_balances_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
            table.HasCheckConstraint("ck_cassette_balances_physical_count", "\"PhysicalCount\" BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "ck_cassette_balances_reserved_count",
                "\"ReservedCount\" BETWEEN 0 AND \"PhysicalCount\"");
        });

        builder.HasKey(balance => balance.Id);
        builder.Property(balance => balance.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(balance => balance.Denomination).IsRequired();
        builder.Property(balance => balance.PhysicalCount).IsRequired();
        builder.Property(balance => balance.ReservedCount).IsRequired();
        builder.HasIndex(balance => new { balance.CassetteId, balance.Currency, balance.Denomination }).IsUnique();
        builder.HasOne(balance => balance.Cassette)
            .WithMany(cassette => cassette.Balances)
            .HasForeignKey(balance => balance.CassetteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
