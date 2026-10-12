using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class CardDailyLimitUsageConfiguration : IEntityTypeConfiguration<CardDailyLimitUsage>
{
    public void Configure(EntityTypeBuilder<CardDailyLimitUsage> builder)
    {
        builder.ToTable("card_daily_limit_usages", table =>
        {
            table.HasCheckConstraint("ck_card_daily_limit_usages_identifier", "length(\"CardIdentifier\") = 64");
            table.HasCheckConstraint("ck_card_daily_limit_usages_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("ck_card_daily_limit_usages_confirmed", "\"ConfirmedAmount\" >= 0");
            table.HasCheckConstraint("ck_card_daily_limit_usages_reserved", "\"ReservedAmount\" >= 0");
        });

        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.CardIdentifier).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(usage => usage.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(usage => usage.BusinessDate).HasColumnType("date").IsRequired();
        builder.Property(usage => usage.ConfirmedAmount).IsRequired();
        builder.Property(usage => usage.ReservedAmount).IsRequired();
        builder.HasIndex(usage => new { usage.CardIdentifier, usage.Currency, usage.BusinessDate }).IsUnique();
    }
}
