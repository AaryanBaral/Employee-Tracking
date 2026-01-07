namespace Tracker.Api.Models;

public class IngestCursor
{
    public string DeviceId { get; set; } = string.Empty;
    public string Stream { get; set; } = string.Empty;
    public long LastSequence { get; set; }
    public Guid LastBatchId { get; set; }
    public DateTimeOffset LastSentAt { get; set; }
}
