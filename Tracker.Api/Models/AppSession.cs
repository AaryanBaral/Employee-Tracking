namespace Tracker.Api.Models;

public class AppSession
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? WindowTitle { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
}
