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
    [property: JsonPropertyName("ok")] bool Ok);

public sealed record LocalVersionResponse(
    [property: JsonPropertyName("contract")] string Contract,
    [property: JsonPropertyName("deviceId")] string? DeviceId,
    [property: JsonPropertyName("agentVersion")] string? AgentVersion,
    [property: JsonPropertyName("port")] int Port);
