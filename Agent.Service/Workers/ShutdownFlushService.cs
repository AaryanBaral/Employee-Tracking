using Agent.Service.Tracking;

namespace Agent.Service.Workers;

public sealed class ShutdownFlushService : IHostedService
{
    private readonly WebSessionizer _webSessionizer;
    private readonly AppSessionizer _appSessionizer;
    private readonly IdleSessionizer _idleSessionizer;
    private readonly ILogger<ShutdownFlushService> _logger;

    public ShutdownFlushService(
        WebSessionizer webSessionizer,
        AppSessionizer appSessionizer,
        IdleSessionizer idleSessionizer,
        ILogger<ShutdownFlushService> logger)
    {
        _webSessionizer = webSessionizer;
        _appSessionizer = appSessionizer;
        _idleSessionizer = idleSessionizer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutdown requested. Closing active sessions.");
        await _webSessionizer.CloseActiveAsync();
        await _appSessionizer.CloseActiveAsync();
        await _idleSessionizer.CloseActiveAsync();
    }
}
