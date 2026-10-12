using AtmCashManagement.Api.Infrastructure.Health;
using AtmCashManagement.Api.Infrastructure.Persistence;
using AtmCashManagement.Api.Application;
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
        services.AddScoped<AtmManagementService>();
        services.AddScoped<CassetteLedgerConsistencyService>();
        services.AddScoped<WithdrawalQuoteService>();
        services.AddScoped<LowCashEventRecorder>();
        services.AddScoped<OutboxEventProcessor>();
        services.AddScoped<OperationsReportService>();
        services.AddSingleton<INoteSelectionService, NoteSelectionService>();
        services.AddSingleton<DepositAllocationPlanner>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<CardIdentifierService>();
        services.AddSingleton<IOutboxEventPublisher, LoggingOutboxEventPublisher>();
        services.AddHostedService<QuoteExpirationBackgroundService>();
        services.AddHostedService<OutboxBackgroundService>();

        return services;
    }
}
