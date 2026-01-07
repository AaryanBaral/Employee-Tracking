using System.Text.Json.Serialization;

namespace Agent.Shared.Models;

public sealed record AppFocusRequest(
    [property: JsonPropertyName("appName")] string AppName,
    [property: JsonPropertyName("windowTitle")] string? WindowTitle,
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc);

public sealed record IdleSampleRequest(
    [property: JsonPropertyName("idleSeconds")] double IdleSeconds,
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc);

public sealed record LocalHealthResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("lastFlushAtUtc")] DateTimeOffset? LastFlushAtUtc = null,
    [property: JsonPropertyName("deviceId")] string? DeviceId = null,
    [property: JsonPropertyName("agentVersion")] string? AgentVersion = null);

public sealed record LocalVersionResponse(
    [property: JsonPropertyName("contract")] string Contract,
    [property: JsonPropertyName("deviceId")] string? DeviceId,
    [property: JsonPropertyName("agentVersion")] string? AgentVersion,
    [property: JsonPropertyName("port")] int Port);

public sealed record LocalDiagResponse(
    [property: JsonPropertyName("contract")] string Contract,
    [property: JsonPropertyName("deviceId")] string? DeviceId,
    [property: JsonPropertyName("agentVersion")] string? AgentVersion,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("serverTimeUtc")] DateTimeOffset ServerTimeUtc,
    [property: JsonPropertyName("outbox")] LocalDiagOutbox Outbox);

public sealed record LocalDiagOutbox(
    [property: JsonPropertyName("pendingByType")] Dictionary<string, int>? PendingByType,
    [property: JsonPropertyName("lastFlushAtUtc")] DateTimeOffset? LastFlushAtUtc,
    [property: JsonPropertyName("available")] bool Available);
