namespace Tracker.Api.Models;

public class WebSession
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
}
