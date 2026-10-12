using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AtmCashManagement.Api.Application;

public sealed class OutboxBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue<int?>("Outbox:PollIntervalSeconds") ?? 5;
        var interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 3600));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<OutboxEventProcessor>();
                    await processor.ProcessPendingBatchAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Pending operations outbox processing failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The periodic wait is cancelled during a normal host shutdown.
        }
    }
}
