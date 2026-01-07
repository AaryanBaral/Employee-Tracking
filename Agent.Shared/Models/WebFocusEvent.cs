namespace Agent.Shared.Models;

public record WebFocusEvent(DateTimeOffset TimestampUtc, string Url, string? Title, string? Browser)
    : ActivityEvent(TimestampUtc);
