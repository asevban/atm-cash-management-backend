using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AtmCashManagement.Api.Infrastructure.Persistence;

public sealed class AtmDbContextFactory : IDesignTimeDbContextFactory<AtmDbContext>
{
    public AtmDbContext CreateDbContext(string[] args)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var apiDirectory = File.Exists(Path.Combine(currentDirectory, "AtmCashManagement.Api.csproj"))
            ? currentDirectory
            : Path.Combine(currentDirectory, "src", "AtmCashManagement.Api");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("AtmDatabase")
            ?? "Host=localhost;Port=5432;Database=atm_cash;Username=atm_app;Password=dev-only-change-me";

        var options = new DbContextOptionsBuilder<AtmDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AtmDbContext(options);
    }
}
