namespace Tracker.Api.Models;

public class WebEvent
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Browser { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
