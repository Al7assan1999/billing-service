using Billing.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Billing.Infrastructure.Jobs;

/// <summary>Runs the billing run on startup and then every hour. Idempotent, so frequent runs are harmless.</summary>
public class BillingRunWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<BillingRunWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
                var result = await scope.ServiceProvider.GetRequiredService<BillingRunService>().RunAsync(today, stoppingToken);
                if (result.InvoicesIssued > 0)
                    logger.LogInformation("Billing run for {Date} issued {Count} invoices", result.RunDate, result.InvoicesIssued);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Billing run failed; will retry next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
