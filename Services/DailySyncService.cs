using medrec.Data;

namespace medrec.Services;

public sealed class DailySyncService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DailySyncService> _logger;

    public DailySyncService(IServiceProvider services, ILogger<DailySyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetDelayUntilNextRun(), stoppingToken);

                using var scope = _services.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<EmrRepository>();
                var count = await repository.ManualSyncAsync();
                _logger.LogInformation("Daily sync completed. {Count} change(s) synced.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Daily sync failed.");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }

    private static TimeSpan GetDelayUntilNextRun()
    {
        var now = DateTime.Now;
        var nextRun = now.Date.AddHours(18);

        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - now;
    }
}
