using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AtmCashManagement.IntegrationTests;

public sealed class LegacyReconciliationMigrationTests
{
    [Fact]
    public async Task Phase7_migration_preserves_and_backfills_existing_reconciliation_rows()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("ATM_TEST_CONNECTION_STRING");
        Assert.False(string.IsNullOrWhiteSpace(adminConnectionString));
        var schema = $"atm_legacy_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var isolatedConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                SearchPath = schema
            }.ConnectionString;
            var options = new DbContextOptionsBuilder<AtmDbContext>()
                .UseNpgsql(isolatedConnectionString)
                .Options;
            await using var db = new AtmDbContext(options);
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261012094000_AddCassetteLedgerIntegrity");

            var atmId = Guid.NewGuid();
            var reconciliationId = Guid.NewGuid();
            await using (var connection = new NpgsqlConnection(isolatedConnectionString))
            {
                await connection.OpenAsync();
                await using var insertAtm = new NpgsqlCommand(
                    "INSERT INTO atms (\"Id\", \"Code\", \"State\", \"CreatedAt\", \"InitialLoadCompletedAt\") " +
                    "VALUES (@id, 'legacy-migration-test', 'Maintenance', @now, NULL)",
                    connection);
                insertAtm.Parameters.AddWithValue("id", atmId);
                insertAtm.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
                await insertAtm.ExecuteNonQueryAsync();

                await using var insertReconciliation = new NpgsqlCommand(
                    "INSERT INTO cash_reconciliations (\"Id\", \"AtmId\", \"OccurredAt\") VALUES (@id, @atm, @now)",
                    connection);
                insertReconciliation.Parameters.AddWithValue("id", reconciliationId);
                insertReconciliation.Parameters.AddWithValue("atm", atmId);
                insertReconciliation.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
                await insertReconciliation.ExecuteNonQueryAsync();
            }

            await migrator.MigrateAsync();
            var migrated = await db.CashReconciliations.AsNoTracking()
                .SingleAsync(reconciliation => reconciliation.Id == reconciliationId);
            Assert.Equal("legacy-unknown", migrated.CreatedBy);
            Assert.Equal(AtmCashManagement.Api.Domain.Enums.CashReconciliationStatus.Completed, migrated.Status);
            Assert.Null(migrated.TotalAbsoluteDifferenceTry);
            Assert.Null(migrated.UsdToTryRateSnapshot);
            Assert.Null(migrated.EurToTryRateSnapshot);
            Assert.Equal(3, await db.ExchangeRates.CountAsync());
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }
}
