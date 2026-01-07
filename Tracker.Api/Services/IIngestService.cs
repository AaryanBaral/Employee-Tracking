using Tracker.Api.Services.Models;

namespace Tracker.Api.Services;

public interface IIngestService
{
    Task<int> InsertWebEventsAsync(IReadOnlyList<WebEventRow> rows, CancellationToken ct);
    Task<int> InsertWebSessionsAsync(IReadOnlyList<WebSessionRow> rows, CancellationToken ct);
    Task<int> InsertAppSessionsAsync(IReadOnlyList<AppSessionRow> rows, CancellationToken ct);
    Task<int> InsertIdleSessionsAsync(IReadOnlyList<IdleSessionRow> rows, CancellationToken ct);
    Task EnsureDevicesAsync(IReadOnlyList<string> deviceIds, DateTimeOffset lastSeenAt, CancellationToken ct);
}
