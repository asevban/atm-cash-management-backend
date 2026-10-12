using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CassetteConfiguration : IEntityTypeConfiguration<Cassette>
{
    public void Configure(EntityTypeBuilder<Cassette> builder)
    {
        builder.ToTable("cassettes", table =>
        {
            table.HasCheckConstraint("ck_cassettes_type", "\"Type\" IN (0, 1)");
            table.HasCheckConstraint("ck_cassettes_number", "\"Number\" BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_cassettes_capacity", "\"Capacity\" BETWEEN 1 AND 100");
            table.HasCheckConstraint(
                "ck_cassettes_currency",
                "\"Currency\" IS NULL OR \"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint(
                "ck_cassettes_type_configuration",
                "(\"Type\" = 0 AND \"Currency\" IS NULL AND \"Denomination\" IS NULL) OR " +
                "(\"Type\" = 1 AND \"Currency\" IS NOT NULL AND \"Denomination\" IN (20, 50, 100, 500))");
        });

        builder.HasKey(cassette => cassette.Id);
        builder.Property(cassette => cassette.Type).IsRequired();
        builder.Property(cassette => cassette.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(cassette => cassette.Denomination);
        builder.Property(cassette => cassette.Capacity).HasDefaultValue(100).IsRequired();
        builder.HasIndex(cassette => new { cassette.AtmId, cassette.Number }).IsUnique();
        builder.HasIndex(cassette => cassette.AtmId)
            .IsUnique()
            .HasFilter($"\"Type\" = {(int)CassetteType.DepositOnly}");
    }
}
