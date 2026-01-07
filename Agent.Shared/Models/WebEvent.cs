namespace Agent.Shared.Models;

public sealed record WebEvent(
    Guid EventId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset Timestamp,
    string Browser
);
