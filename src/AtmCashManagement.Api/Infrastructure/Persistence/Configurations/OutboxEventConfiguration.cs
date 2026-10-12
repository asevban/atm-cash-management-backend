using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events", table =>
        {
            table.HasCheckConstraint("ck_outbox_events_status", "\"Status\" IN ('Pending', 'Sent')");
            table.HasCheckConstraint("ck_outbox_events_retry_count", "\"RetryCount\" >= 0");
            table.HasCheckConstraint(
                "ck_outbox_events_sent_at",
                "(\"Status\" = 'Pending' AND \"SentAtUtc\" IS NULL) OR (\"Status\" = 'Sent' AND \"SentAtUtc\" IS NOT NULL)");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.EventType).HasMaxLength(60).IsRequired();
        builder.Property(item => item.EventKey).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(item => item.CreatedAtUtc).IsRequired();
        builder.Property(item => item.RetryCount).IsRequired();
        builder.HasIndex(item => item.EventKey).IsUnique();
        builder.HasIndex(item => new { item.Status, item.CreatedAtUtc });
        builder.HasOne(item => item.Atm)
            .WithMany()
            .HasForeignKey(item => item.AtmId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Cassette)
            .WithMany()
            .HasForeignKey(item => item.CassetteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
