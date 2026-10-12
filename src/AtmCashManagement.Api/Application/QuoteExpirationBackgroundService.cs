using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AtmCashManagement.Api.Application;

public sealed class QuoteExpirationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<QuoteExpirationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var quoteService = scope.ServiceProvider.GetRequiredService<WithdrawalQuoteService>();
                await quoteService.ExpireDueQuotesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Expired withdrawal quote sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
