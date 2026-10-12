using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates", table => table.HasCheckConstraint(
            "ck_exchange_rates_supported_pair",
            "((\"BaseCurrency\" = 'TRY' AND \"QuoteCurrency\" = 'TRY' AND \"Rate\" = 1) OR (\"BaseCurrency\" IN ('USD', 'EUR') AND \"QuoteCurrency\" = 'TRY' AND \"Rate\" > 0))"));
        builder.HasKey(rate => new { rate.BaseCurrency, rate.QuoteCurrency });
        builder.Property(rate => rate.BaseCurrency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(rate => rate.QuoteCurrency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(rate => rate.Rate).HasPrecision(18, 6).IsRequired();
        builder.HasData(
            new ExchangeRate(Currency.TRY, Currency.TRY, 1m),
            new ExchangeRate(Currency.USD, Currency.TRY, 35m),
            new ExchangeRate(Currency.EUR, Currency.TRY, 38m));
    }
}
