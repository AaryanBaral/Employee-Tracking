namespace Agent.Shared.Models;

public record IdleEvent(DateTimeOffset TimestampUtc, TimeSpan IdleDuration)
    : ActivityEvent(TimestampUtc);
