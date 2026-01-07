namespace Tracker.Api.Contracts;

public sealed record WebSessionIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    IReadOnlyList<WebSessionDto> Sessions);

public sealed record AppSessionIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    IReadOnlyList<AppSessionDto> Sessions);

public sealed record IdleSessionIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    IReadOnlyList<IdleSessionDto> Sessions);

public sealed record DeviceHeartbeatRequest(
    string DeviceId,
    string? Hostname,
    DateTimeOffset LastSeenAt);

public sealed record UpdateDeviceRequest(string? DisplayName, bool? IsSeen);

public sealed record IngestResponse(
    int Received,
    int Invalid,
    int Inserted,
    int DuplicatesIgnored);

public sealed record WebEventIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    Guid EventId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset Timestamp,
    string? Browser);

public sealed record WebEventBatchItem(
    Guid EventId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset Timestamp,
    string? Browser);

public sealed record WebEventBatchRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    IReadOnlyList<WebEventBatchItem> Events);

public sealed record DeviceListItem(
    string DeviceId,
    string? Hostname,
    string? DisplayName,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastReviewedAt);

public sealed record DeviceSummaryDevice(
    string DeviceId,
    string? Hostname,
    string? DisplayName,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastReviewedAt);

public sealed record DeviceSummaryItem(string Name, long Seconds);

public sealed record DeviceSummaryResponse(
    string Date,
    DeviceSummaryDevice Device,
    long IdleSeconds,
    IReadOnlyList<DeviceSummaryItem> TopDomains,
    IReadOnlyList<DeviceSummaryItem> TopUrls,
    IReadOnlyList<DeviceSummaryItem> TopApps);

public sealed record WebSessionDto(
    Guid SessionId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record WebSessionListItem(
    Guid SessionId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record AppSessionListItem(
    Guid SessionId,
    string ProcessName,
    string? WindowTitle,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record IdleSessionListItem(Guid SessionId, DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record AppSessionDto(
    Guid SessionId,
    string ProcessName,
    string? WindowTitle,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record IdleSessionDto(Guid SessionId, DateTimeOffset StartAt, DateTimeOffset EndAt);
