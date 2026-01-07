namespace Agent.Shared.Models;

public record WebSessionDto(
    Guid SessionId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt
);

public record AppSessionDto(
    Guid SessionId,
    string ProcessName,
    string? WindowTitle,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt
);

public record IdleSessionDto(
    Guid SessionId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt
);

public record WebEventIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    Guid EventId,
    string Domain,
    string Title,
    string Url,
    DateTimeOffset Timestamp,
    string Browser
);

public record WebEventBatchItem(
    Guid EventId,
    string Domain,
    string? Title,
    string? Url,
    DateTimeOffset Timestamp,
    string Browser
);

public record WebEventBatchRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    List<WebEventBatchItem> Events
);

public record WebSessionIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    List<WebSessionDto> Sessions
);

public record AppSessionIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    List<AppSessionDto> Sessions
);

public record IdleSessionIngestRequest(
    string DeviceId,
    string AgentVersion,
    Guid BatchId,
    long Sequence,
    DateTimeOffset SentAt,
    List<IdleSessionDto> Sessions
);
