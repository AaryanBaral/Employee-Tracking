using Agent.Shared.Abstractions;
using Agent.Service.Tracking;
using Agent.Shared.Config;
using Microsoft.Extensions.Options;

namespace Agent.Service.Workers;

public sealed class CollectorWorker : BackgroundService
{
    private readonly IIdleCollector _idleCollector;
    private readonly IAppCollector _appCollector;
    private readonly AppSessionizer _appSessionizer;
    private readonly IdleSessionizer _idleSessionizer;
    private readonly WebSessionizer _webSessionizer;
    private readonly AgentConfig _config;
    private readonly ILogger<CollectorWorker> _logger;

    public CollectorWorker(
        IIdleCollector idleCollector,
        IAppCollector appCollector,
        AppSessionizer appSessionizer,
        IdleSessionizer idleSessionizer,
        WebSessionizer webSessionizer,
        IOptions<AgentConfig> config,
        ILogger<CollectorWorker> logger)
    {
        _idleCollector = idleCollector;
        _appCollector = appCollector;
        _appSessionizer = appSessionizer;
        _idleSessionizer = idleSessionizer;
        _webSessionizer = webSessionizer;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Max(1, _config.CollectorPollSeconds);
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                // 1. Idle
                var idle = await _idleCollector.GetIdleAsync(stoppingToken);
                if (idle is not null)
                {
                    if (_config.LogIdleEvents)
                    {
                        _logger.LogDebug("Idle sample: {idleSeconds:n2}s", idle.IdleDuration.TotalSeconds);
                    }

                    await _idleSessionizer.HandleIdleStateAsync(idle.IdleDuration, now);
                    await _webSessionizer.HandleIdleStateAsync(idle.IdleDuration, now);
                }

                // 2. App
                var app = await _appCollector.GetFocusedAppAsync(stoppingToken);
                if (app is not null && !string.IsNullOrWhiteSpace(app.AppName))
                {
                    if (_config.LogAppEvents)
                    {
                        _logger.LogDebug("App focus sample: {app} | {title}", app.AppName, app.WindowTitle);
                    }

                    await _appSessionizer.HandleAppFocusAsync(app.AppName, now, app.WindowTitle);
                    await _webSessionizer.HandleAppFocusAsync(app.AppName, now);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CollectorWorker loop.");
            }
        }

        // Shutdown Drain
        _logger.LogInformation("CollectorWorker shutting down. closing sessions.");
        try
        {
            await _appSessionizer.CloseActiveAsync();
            await _idleSessionizer.CloseActiveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error draining sessions on shutdown.");
        }
    }
}
