using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCashManagement.Api.Infrastructure.Persistence.Configurations;

public sealed class ConfirmIdempotencyRecordConfiguration : IEntityTypeConfiguration<ConfirmIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ConfirmIdempotencyRecord> builder)
    {
        builder.ToTable("confirm_idempotency_records");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Key).HasMaxLength(200).IsRequired();
        builder.Property(record => record.OccurredAtUtc).IsRequired();
        builder.HasIndex(record => record.Key).IsUnique();
        builder.HasOne<WithdrawalQuote>().WithMany().HasForeignKey(record => record.QuoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WithdrawalQuoteOption>().WithMany().HasForeignKey(record => record.SelectedOptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(record => record.Withdrawal).WithMany().HasForeignKey(record => record.WithdrawalId).OnDelete(DeleteBehavior.Restrict);
    }
}
