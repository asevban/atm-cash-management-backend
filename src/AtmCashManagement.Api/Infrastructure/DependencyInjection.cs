using AtmCashManagement.Api.Infrastructure.Health;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmCashManagement.Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AtmDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:AtmDatabase is not configured.");
        }

        services.AddDbContext<AtmDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        return services;
    }
}
