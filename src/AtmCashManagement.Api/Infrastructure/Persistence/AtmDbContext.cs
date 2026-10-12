using AtmCashManagement.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtmCashManagement.Api.Infrastructure.Persistence;

public sealed class AtmDbContext(DbContextOptions<AtmDbContext> options) : DbContext(options)
{
    public DbSet<Atm> Atms => Set<Atm>();
    public DbSet<Cassette> Cassettes => Set<Cassette>();
    public DbSet<CassetteBalance> CassetteBalances => Set<CassetteBalance>();
    public DbSet<CassetteMovement> CassetteMovements => Set<CassetteMovement>();
    public DbSet<CashDeposit> CashDeposits => Set<CashDeposit>();
    public DbSet<CashDepositNote> CashDepositNotes => Set<CashDepositNote>();
    public DbSet<CashReconciliation> CashReconciliations => Set<CashReconciliation>();
    public DbSet<CashReconciliationLine> CashReconciliationLines => Set<CashReconciliationLine>();
    public DbSet<WithdrawalQuote> WithdrawalQuotes => Set<WithdrawalQuote>();
    public DbSet<WithdrawalQuoteOption> WithdrawalQuoteOptions => Set<WithdrawalQuoteOption>();
    public DbSet<WithdrawalQuoteOptionAllocation> WithdrawalQuoteOptionAllocations => Set<WithdrawalQuoteOptionAllocation>();
    public DbSet<QuoteReservation> QuoteReservations => Set<QuoteReservation>();
    public DbSet<CardDailyLimitUsage> CardDailyLimitUsages => Set<CardDailyLimitUsage>();
    public DbSet<CashWithdrawal> CashWithdrawals => Set<CashWithdrawal>();
    public DbSet<ConfirmIdempotencyRecord> ConfirmIdempotencyRecords => Set<ConfirmIdempotencyRecord>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureCassetteMovementsAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureCassetteMovementsAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtmDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    private void EnsureCassetteMovementsAreAppendOnly()
    {
        var changedMovement = ChangeTracker.Entries<CassetteMovement>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (changedMovement is not null)
        {
            throw new InvalidOperationException("Cassette movement ledger rows are append-only and cannot be updated or deleted.");
        }
    }
}
