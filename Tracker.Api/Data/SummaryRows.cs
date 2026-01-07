namespace Tracker.Api.Data;

public sealed class IdleSecondsRow
{
    public long Seconds { get; set; }
}

public sealed class DomainSummaryRow
{
    public string Domain { get; set; } = string.Empty;
    public long Seconds { get; set; }
}

public sealed class AppSummaryRow
{
    public string ProcessName { get; set; } = string.Empty;
    public long Seconds { get; set; }
}

public sealed class UrlSummaryRow
{
    public string Url { get; set; } = string.Empty;
    public long Seconds { get; set; }
}
