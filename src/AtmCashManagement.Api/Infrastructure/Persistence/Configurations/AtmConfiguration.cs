using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class AtmConfiguration : IEntityTypeConfiguration<Atm>
{
    public void Configure(EntityTypeBuilder<Atm> builder)
    {
        builder.ToTable("atms", table =>
            table.HasCheckConstraint(
                "ck_atms_state",
                "\"State\" IN ('Unconfigured', 'Configured', 'Loaded', 'InService', 'Maintenance')"));
        builder.HasKey(atm => atm.Id);
        builder.Property(atm => atm.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(atm => atm.Code).IsUnique();
        builder.Property(atm => atm.State).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(atm => atm.CreatedAt).IsRequired();
        builder.HasMany(atm => atm.Cassettes)
            .WithOne(cassette => cassette.Atm)
            .HasForeignKey(cassette => cassette.AtmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
