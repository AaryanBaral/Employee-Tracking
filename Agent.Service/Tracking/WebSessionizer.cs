using Agent.Service.Infrastructure;
using Agent.Shared.Models;

namespace Agent.Service.Tracking;

public sealed class WebSessionizer
{
    private readonly IOutboxService _outbox;
    private readonly ILogger<WebSessionizer> _logger;
    private readonly object _lock = new();
    private ActiveWebSession? _active;
    private CurrentTab? _currentTab;
    private string? _deviceId;
    private DateTimeOffset? _lastBrowserActiveAt;
    private DateTimeOffset? _lastUserActiveAt;
    private string? _lastForegroundApp;
    private DateTimeOffset? _lastForegroundAppAt;
    private readonly HashSet<string> _knownBrowserApps = new(StringComparer.OrdinalIgnoreCase);

    private const double IdleThresholdSeconds = 60.0;
    private static readonly TimeSpan ForegroundGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IdleGrace = TimeSpan.FromSeconds(3);

    public WebSessionizer(IOutboxService outbox, ILogger<WebSessionizer> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public async Task HandleEventAsync(WebEvent evt, string deviceId)
    {
        // 1. Log raw event to outbox
        await _outbox.EnqueueAsync("web_event", evt);

        var ts = evt.Timestamp == default ? DateTimeOffset.UtcNow : evt.Timestamp;
        
        WebSessionRecord? sessionToClose = null;

        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(_lastForegroundApp) &&
                _lastForegroundAppAt.HasValue &&
                ts - _lastForegroundAppAt.Value <= ForegroundGrace)
            {
                _knownBrowserApps.Add(_lastForegroundApp);
                _lastBrowserActiveAt = _lastForegroundAppAt.Value;
            }

            _deviceId = deviceId;
            var browser = string.IsNullOrWhiteSpace(evt.Browser) ? "chromium" : evt.Browser;
            _currentTab = new CurrentTab(evt.Domain, evt.Url, evt.Title, browser, ts);
            sessionToClose = EvaluateSessionLocked(ts);
        }

        await EnqueueIfValidAsync(sessionToClose);
    }

    public async Task HandleAppFocusAsync(string appName, DateTimeOffset timestamp)
    {
        WebSessionRecord? sessionToClose = null;
        lock (_lock)
        {
            _lastForegroundApp = appName;
            _lastForegroundAppAt = timestamp;

            if (_knownBrowserApps.Contains(appName))
            {
                _lastBrowserActiveAt = timestamp;
            }

            sessionToClose = EvaluateSessionLocked(timestamp);
        }

        await EnqueueIfValidAsync(sessionToClose);
    }

    public async Task HandleIdleStateAsync(TimeSpan idleDuration, DateTimeOffset timestamp)
    {
        WebSessionRecord? sessionToClose = null;
        lock (_lock)
        {
            if (idleDuration.TotalSeconds < IdleThresholdSeconds)
            {
                _lastUserActiveAt = timestamp;
            }

            sessionToClose = EvaluateSessionLocked(timestamp);
        }

        await EnqueueIfValidAsync(sessionToClose);
    }

    public async Task CloseActiveAsync()
    {
        WebSessionRecord? sessionToClose = null;
        lock (_lock)
        {
            if (_active is null) return;

            var now = DateTimeOffset.UtcNow;
            sessionToClose = new WebSessionRecord(
                Guid.NewGuid(),
                _active.DeviceId,
                _active.Domain,
                _active.Title,
                _active.Url,
                _active.StartUtc,
                now);
            
            _active = null;
        }

        await EnqueueIfValidAsync(sessionToClose);
    }

    private async Task EnqueueIfValidAsync(WebSessionRecord? sessionToClose)
    {
        if (sessionToClose is null)
        {
            return;
        }

        if ((sessionToClose.EndAtUtc - sessionToClose.StartAtUtc) < TimeSpan.FromSeconds(1))
        {
            return;
        }

        await _outbox.EnqueueAsync("web_session", sessionToClose);
    }

    private WebSessionRecord? EvaluateSessionLocked(DateTimeOffset now)
    {
        var isActiveWindow = IsBrowserActive(now);
        var isUserActive = IsUserActive(now);
        var hasTab = _currentTab is not null && !string.IsNullOrWhiteSpace(_currentTab.Url);
        var shouldBeActive = isActiveWindow && isUserActive && hasTab;

        if (shouldBeActive && _currentTab is not null && !string.IsNullOrWhiteSpace(_deviceId))
        {
            if (_active is null)
            {
                _active = new ActiveWebSession
                {
                    DeviceId = _deviceId,
                    Browser = _currentTab.Browser,
                    Domain = _currentTab.Domain,
                    Url = _currentTab.Url,
                    Title = _currentTab.Title,
                    StartUtc = now,
                    LastSeenUtc = now
                };
                return null;
            }

            if (!IsSamePage(_active, _currentTab))
            {
                var end = now < _active.StartUtc ? DateTimeOffset.UtcNow : now;
                var sessionToClose = new WebSessionRecord(
                    Guid.NewGuid(),
                    _active.DeviceId,
                    _active.Domain,
                    _active.Title,
                    _active.Url,
                    _active.StartUtc,
                    end);

                _active = new ActiveWebSession
                {
                    DeviceId = _active.DeviceId,
                    Browser = _currentTab.Browser,
                    Domain = _currentTab.Domain,
                    Url = _currentTab.Url,
                    Title = _currentTab.Title,
                    StartUtc = now,
                    LastSeenUtc = now
                };

                return sessionToClose;
            }

            _active.LastSeenUtc = now;
            if (!string.IsNullOrWhiteSpace(_currentTab.Title))
            {
                _active.Title = _currentTab.Title;
            }
            if (!string.IsNullOrWhiteSpace(_currentTab.Url))
            {
                _active.Url = _currentTab.Url;
            }

            return null;
        }

        if (_active is null)
        {
            return null;
        }

        var inactiveEnd = GetInactiveEndAt(now);
        if (inactiveEnd < _active.StartUtc)
        {
            inactiveEnd = now;
        }

        var toClose = new WebSessionRecord(
            Guid.NewGuid(),
            _active.DeviceId,
            _active.Domain,
            _active.Title,
            _active.Url,
            _active.StartUtc,
            inactiveEnd);

        _active = null;
        return toClose;
    }

    private bool IsBrowserActive(DateTimeOffset now)
    {
        if (!_lastBrowserActiveAt.HasValue)
        {
            return false;
        }

        return now - _lastBrowserActiveAt.Value <= ForegroundGrace;
    }

    private bool IsUserActive(DateTimeOffset now)
    {
        if (!_lastUserActiveAt.HasValue)
        {
            return false;
        }

        return now - _lastUserActiveAt.Value <= IdleGrace;
    }

    private static DateTimeOffset MinTimestamp(DateTimeOffset a, DateTimeOffset b)
    {
        return a <= b ? a : b;
    }

    private DateTimeOffset GetInactiveEndAt(DateTimeOffset now)
    {
        var endAt = now;
        if (_lastBrowserActiveAt.HasValue)
        {
            endAt = MinTimestamp(endAt, _lastBrowserActiveAt.Value);
        }
        if (_lastUserActiveAt.HasValue)
        {
            endAt = MinTimestamp(endAt, _lastUserActiveAt.Value);
        }

        return endAt;
    }

    private static bool IsSamePage(ActiveWebSession active, CurrentTab tab)
    {
        var urlSame = string.Equals(active.Url ?? "", tab.Url ?? "", StringComparison.Ordinal);
        var domainSame = string.Equals(active.Domain, tab.Domain, StringComparison.Ordinal);
        var browserSame = string.Equals(
            active.Browser,
            tab.Browser,
            StringComparison.OrdinalIgnoreCase);

        return browserSame && domainSame && urlSame;
    }

    private sealed record CurrentTab(
        string Domain,
        string? Url,
        string? Title,
        string Browser,
        DateTimeOffset LastEventAtUtc);

    private sealed class ActiveWebSession
    {
        public required string DeviceId { get; init; }
        public required string Browser { get; init; }
        public required string Domain { get; init; }
        public string? Url { get; set; }
        public string? Title { get; set; }
        public required DateTimeOffset StartUtc { get; init; }
        public required DateTimeOffset LastSeenUtc { get; set; }
    }
}

public sealed record WebSessionRecord(
    Guid SessionId,
    string DeviceId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc);
