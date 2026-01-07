using System.Threading;

namespace Agent.Service.Infrastructure;

public sealed class OutboxSenderState
{
    private long _lastFlushUnixMs;

    public DateTimeOffset? LastFlushAtUtc
    {
        get
        {
            var value = Interlocked.Read(ref _lastFlushUnixMs);
            return value == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(value);
        }
    }

    public void MarkFlushed(DateTimeOffset atUtc)
    {
        var unixMs = atUtc.ToUnixTimeMilliseconds();
        Interlocked.Exchange(ref _lastFlushUnixMs, unixMs);
    }
}
