namespace Agent.Shared.Models;

public record AppFocusEvent(DateTimeOffset TimestampUtc, string AppName, string? WindowTitle)
    : ActivityEvent(TimestampUtc);
