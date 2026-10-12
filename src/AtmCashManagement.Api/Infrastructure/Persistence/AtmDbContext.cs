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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtmDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
