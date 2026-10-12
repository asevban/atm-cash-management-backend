using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AtmCashManagement.Api.Infrastructure.Health;

public sealed class DatabaseHealthCheck(AtmDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL connection is available.")
                : HealthCheckResult.Unhealthy("PostgreSQL connection is unavailable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection check failed.", exception);
        }
    }
}
