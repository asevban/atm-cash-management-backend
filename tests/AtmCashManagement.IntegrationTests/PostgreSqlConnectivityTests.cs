using Npgsql;

namespace AtmCashManagement.IntegrationTests;

public sealed class PostgreSqlConnectivityTests
{
    [Fact]
    public async Task Configured_postgresql_database_accepts_a_query()
    {
        var connectionString = Environment.GetEnvironmentVariable("ATM_TEST_CONNECTION_STRING");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set ATM_TEST_CONNECTION_STRING to a real PostgreSQL database before running integration tests.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(1, result);
    }
}
