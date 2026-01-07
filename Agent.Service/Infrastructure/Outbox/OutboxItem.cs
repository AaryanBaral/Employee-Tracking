namespace Agent.Service.Infrastructure.Outbox;

public record OutboxItem
{
    public long Id { get; init; }
    public required string Type { get; init; }
    public required string PayloadJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
