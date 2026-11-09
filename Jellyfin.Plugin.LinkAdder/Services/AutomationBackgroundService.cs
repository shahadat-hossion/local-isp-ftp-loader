using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LinkAdder.Services;

public sealed class AutomationBackgroundService : BackgroundService
{
    private readonly AutomationService _automation;
    private readonly ILogger<AutomationBackgroundService> _logger;

    public AutomationBackgroundService(AutomationService automation, ILogger<AutomationBackgroundService> logger)
    {
        _automation = automation;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        try
        {
            await _automation.WarmSearchIndexesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local ISP FTP Loader search-index warmup failed.");
        }
        await Task.Delay(TimeSpan.FromSeconds(110), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var options = Plugin.Instance?.Configuration.Automation;
                if (options?.Enabled == true && (options.LastRunUtc is null || DateTime.UtcNow >= options.LastRunUtc.Value.AddHours(Math.Clamp(options.IntervalHours, 1, 168))))
                    await _automation.RunAsync(false, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local ISP FTP Loader automation run failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken).ConfigureAwait(false);
        }
    }
}
