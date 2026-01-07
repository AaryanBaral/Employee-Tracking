namespace Tracker.Api.Models;

public class IdleSession
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
}
