namespace Tracker.Api.Infrastructure;

public static class IngestLimits
{
    public const int MaxSessionsPerRequest = 5000;
    public const int MaxWebEventsPerRequest = 5000;
    public const int InsertChunkSize = 500;
}
