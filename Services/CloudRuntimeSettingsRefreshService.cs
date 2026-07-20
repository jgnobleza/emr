using medrec.Data;

namespace medrec.Services;

public sealed class CloudRuntimeSettingsRefreshService : BackgroundService
{
    private readonly RuntimeSettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CloudRuntimeSettingsRefreshService> _logger;

    public CloudRuntimeSettingsRefreshService(
        RuntimeSettingsService settings,
        IConfiguration configuration,
        ILogger<CloudRuntimeSettingsRefreshService> logger)
    {
        _settings = settings;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                if (_configuration.GetSection("MedRec").Get<MedRecStorageOptions>()?.UseLocalStorage == true)
                {
                    continue;
                }

                await _settings.LoadCloudSettingsAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to refresh shared runtime settings from PostgreSQL.");
            }
        }
    }
}
