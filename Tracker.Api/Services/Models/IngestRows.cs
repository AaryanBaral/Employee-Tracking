namespace Tracker.Api.Services.Models;

public sealed record WebEventRow(
    Guid EventId,
    string DeviceId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset Timestamp,
    string? Browser,
    DateTimeOffset ReceivedAt);

public sealed record WebSessionRow(
    Guid SessionId,
    string DeviceId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record AppSessionRow(
    Guid SessionId,
    string DeviceId,
    string ProcessName,
    string? WindowTitle,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record IdleSessionRow(
    Guid SessionId,
    string DeviceId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);
