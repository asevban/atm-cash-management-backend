using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Npgsql;

namespace AtmCashManagement.IntegrationTests;

public sealed class AtmApiFixture : IAsyncLifetime
{
    private readonly string _adminConnectionString;
    private string _schema = string.Empty;
    private WebApplicationFactory<Program>? _factory;

    public AtmApiFixture()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable("ATM_TEST_CONNECTION_STRING") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            throw new InvalidOperationException("Set ATM_TEST_CONNECTION_STRING to a dedicated real PostgreSQL test database.");
        }
    }

    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => _factory?.Services ?? throw new InvalidOperationException("The API test host is not initialized.");

    public async Task InitializeAsync()
    {
        _schema = $"atm_test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var isolatedConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            SearchPath = _schema
        }.ConnectionString;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AtmDatabase"] = isolatedConnectionString
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AtmDbContext>();
                services.RemoveAll<DbContextOptions<AtmDbContext>>();
                services.AddDbContext<AtmDbContext>(options => options.UseNpgsql(isolatedConnectionString));
            });
        });
        Client = _factory.CreateClient();

        using var healthResponse = await Client.GetAsync("/health");
        healthResponse.EnsureSuccessStatusCode();
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            SearchPath = _schema
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "TRUNCATE TABLE cash_reconciliation_lines, cash_reconciliations, cash_deposit_notes, cash_deposits, " +
            "cassette_movements, cassette_balances, cassettes, atms CASCADE",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> GetInitialLoadMovementCountAsync()
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            SearchPath = _schema
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM cassette_movements WHERE \"Type\" = 'Load' AND \"IsInitialLoad\"",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task SetReservedCountAsync(
        Guid atmId,
        int cassetteNumber,
        AtmCashManagement.Api.Domain.Enums.Currency currency,
        AtmCashManagement.Api.Domain.Enums.BanknoteDenomination denomination,
        int reservedCount)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            SearchPath = _schema
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE cassette_balances SET \"ReservedCount\" = @reserved " +
            "WHERE \"CassetteId\" = (SELECT \"Id\" FROM cassettes WHERE \"AtmId\" = @atm AND \"Number\" = @number) " +
            "AND \"Currency\" = @currency AND \"Denomination\" = @denomination",
            connection);
        command.Parameters.AddWithValue("reserved", reservedCount);
        command.Parameters.AddWithValue("atm", atmId);
        command.Parameters.AddWithValue("number", cassetteNumber);
        command.Parameters.AddWithValue("currency", currency.ToString());
        command.Parameters.AddWithValue("denomination", (short)denomination);
        if (await command.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException("The requested cassette balance row was not found.");
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();
        if (string.IsNullOrWhiteSpace(_schema))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
