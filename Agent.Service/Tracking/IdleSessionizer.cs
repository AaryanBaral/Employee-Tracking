using Agent.Service.Infrastructure;
using Agent.Service.Identity;

namespace Agent.Service.Tracking;

public sealed record IdleSessionRecord(
    Guid SessionId,
    string DeviceId,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc
);

public sealed class IdleSessionizer
{
    private readonly IOutboxService _outbox;
    private readonly DeviceIdentityStore _identityStore;
    private readonly ILogger<IdleSessionizer> _logger;

    private bool _isIdle;
    private DateTimeOffset? _idleStartUtc;
    private readonly double _thresholdSeconds; // Could be config driven, but typically hardcoded or simple config

    public IdleSessionizer(
        IOutboxService outbox, 
        DeviceIdentityStore identityStore,
        ILogger<IdleSessionizer> logger)
    {
        _outbox = outbox;
        _identityStore = identityStore;
        _logger = logger;
        _thresholdSeconds = 60.0; // Default idle threshold
    }

    public async Task HandleIdleStateAsync(TimeSpan idleDuration, DateTimeOffset timestamp)
    {
        var isIdleNow = idleDuration.TotalSeconds >= _thresholdSeconds;

        if (!_isIdle && isIdleNow)
        {
            // Transition: Active -> Idle
            // IMPORTANT: Start time is effectively in the past
            // We subtract the accumulated duration to find when it started
            _idleStartUtc = timestamp - idleDuration;
            _isIdle = true;
            _logger.LogInformation("IDLE START @ {start}", _idleStartUtc);
        }
        else if (_isIdle && !isIdleNow)
        {
            // Transition: Idle -> Active
            // End time is roughly now (or timestamp)
            var start = _idleStartUtc ?? timestamp.AddSeconds(-_thresholdSeconds);
            var end = timestamp;
            
            _isIdle = false;
            _idleStartUtc = null;

            var identity = await _identityStore.GetOrCreateAsync(CancellationToken.None);
            var record = new IdleSessionRecord(
                Guid.NewGuid(),
                identity.DeviceId.ToString(),
                start,
                end
            );

            await _outbox.EnqueueAsync("idle_session", record);
            _logger.LogInformation(
                "IDLE END {start} -> {end} (secs={secs:n0})",
                record.StartAtUtc,
                record.EndAtUtc,
                (record.EndAtUtc - record.StartAtUtc).TotalSeconds);
        }
        else if (_isIdle && isIdleNow)
        {
            // Still idle. No op, or update "last check" if we implement heartbeat-ish idle checks?
            // Currently we only emit on close.
        }
    }

    public async Task CloseActiveAsync()
    {
        if (_isIdle && _idleStartUtc.HasValue)
        {
            var end = DateTimeOffset.UtcNow;
            var identity = await _identityStore.GetOrCreateAsync(CancellationToken.None);
            
            var record = new IdleSessionRecord(
                Guid.NewGuid(),
                identity.DeviceId.ToString(),
                _idleStartUtc.Value,
                end
            );
            
            await _outbox.EnqueueAsync("idle_session", record);
            _logger.LogInformation(
                "IDLE END {start} -> {end} (secs={secs:n0})",
                record.StartAtUtc,
                record.EndAtUtc,
                (record.EndAtUtc - record.StartAtUtc).TotalSeconds);
            _isIdle = false;
            _idleStartUtc = null;
        }
    }
}
