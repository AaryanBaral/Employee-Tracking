namespace Agent.Service.Tracking;

public sealed record ActiveAppSession
{
    public required string DeviceId { get; init; }
    public required string AppName { get; init; }
    public string? WindowTitle { get; set; }
    public required DateTimeOffset StartUtc { get; init; } // When app became foreground
    public required DateTimeOffset LastSeenUtc { get; set; }
}

public sealed record AppSessionRecord(
    Guid SessionId,
    string DeviceId,
    string AppName,
    string? WindowTitle,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc
);
