using Agent.Service.Infrastructure;
using Agent.Service.Identity;
using Microsoft.Extensions.Logging;

namespace Agent.Service.Tracking;

public sealed class AppSessionizer
{
    private readonly IOutboxService _outbox;
    private readonly DeviceIdentityStore _identityStore;
    private readonly ILogger<AppSessionizer> _logger;
    private ActiveAppSession? _active;
    private readonly object _lock = new();

    public AppSessionizer(IOutboxService outbox, DeviceIdentityStore identityStore, ILogger<AppSessionizer> logger)
    {
        _outbox = outbox;
        _identityStore = identityStore;
        _logger = logger;
    }

    public async Task HandleAppFocusAsync(string appName, DateTimeOffset timestamp, string? windowTitle = null)
    {
        var identity = await _identityStore.GetOrCreateAsync(CancellationToken.None);
        var deviceId = identity.DeviceId.ToString();

        // Fire-and-forget outbox enqueue to avoid blocking the collector loop too long?
        // Actually, CollectorWorker awaits execution, so we should await here to propagate pressure/errors appropriately.
        // We will return Task.

        AppSessionRecord? toEnqueue = null;
        string? startApp = null;
        string? startTitle = null;
        DateTimeOffset startAt = default;

        lock (_lock)
        {
            if (_active is not null && string.Equals(_active.AppName, appName, StringComparison.OrdinalIgnoreCase))
            {
                // Same app, just update LastSeen
                _active.LastSeenUtc = timestamp;
                if (!string.IsNullOrWhiteSpace(windowTitle))
                {
                    _active.WindowTitle = windowTitle;
                }
                return;
            }

            if (_active is not null)
            {
                // Different app, close previous
                var end = timestamp;
                if (end < _active.StartUtc) end = DateTimeOffset.UtcNow; // clock skew protection

                toEnqueue = new AppSessionRecord(
                    Guid.NewGuid(),
                    _active.DeviceId,
                    _active.AppName,
                    _active.WindowTitle,
                    _active.StartUtc,
                    end
                );
            }

            // Start new
            _active = new ActiveAppSession
            {
                DeviceId = deviceId,
                AppName = appName,
                WindowTitle = windowTitle,
                StartUtc = timestamp,
                LastSeenUtc = timestamp
            };
            startApp = appName;
            startTitle = windowTitle;
            startAt = timestamp;
        }

        if (startApp is not null)
        {
            _logger.LogInformation("APP START: {app} | {title} @ {start}", startApp, startTitle, startAt);
        }

        if (toEnqueue is not null)
        {
            await _outbox.EnqueueAsync("app_session", toEnqueue);
            _logger.LogInformation(
                "APP END: {app} | {title} {start} -> {end} (secs={secs:n0})",
                toEnqueue.AppName,
                toEnqueue.WindowTitle,
                toEnqueue.StartUtc,
                toEnqueue.EndUtc,
                (toEnqueue.EndUtc - toEnqueue.StartUtc).TotalSeconds);
        }
    }

    public async Task CloseActiveAsync()
    {
        AppSessionRecord? toEnqueue = null;
        lock (_lock)
        {
            if (_active is null) return;

            var now = DateTimeOffset.UtcNow;
            toEnqueue = new AppSessionRecord(
                Guid.NewGuid(),
                _active.DeviceId,
                _active.AppName,
                _active.WindowTitle,
                _active.StartUtc,
                now
            );
            _active = null;
        }

        if (toEnqueue is not null)
        {
            await _outbox.EnqueueAsync("app_session", toEnqueue);
            _logger.LogInformation(
                "APP END: {app} | {title} {start} -> {end} (secs={secs:n0})",
                toEnqueue.AppName,
                toEnqueue.WindowTitle,
                toEnqueue.StartUtc,
                toEnqueue.EndUtc,
                (toEnqueue.EndUtc - toEnqueue.StartUtc).TotalSeconds);
        }
    }
}
