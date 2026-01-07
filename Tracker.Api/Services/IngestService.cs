using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tracker.Api.Data;
using Tracker.Api.Infrastructure;
using Tracker.Api.Models;
using Tracker.Api.Services.Models;

namespace Tracker.Api.Services;

public sealed class IngestService : IIngestService
{
    private readonly TrackerDbContext _db;

    public IngestService(TrackerDbContext db)
    {
        _db = db;
    }

    public Task<int> InsertWebEventsAsync(IReadOnlyList<WebEventRow> rows, CancellationToken ct)
    {
        var values = rows.Select(r => new object?[]
        {
            Guid.NewGuid(),
            r.EventId,
            r.DeviceId,
            r.Domain,
            r.Title,
            r.Url,
            r.Timestamp,
            r.Browser,
            r.ReceivedAt
        }).ToList();

        return InsertRowsAsync(
            "WebEvents",
            new[]
            {
                "Id",
                "EventId",
                "DeviceId",
                "Domain",
                "Title",
                "Url",
                "Timestamp",
                "Browser",
                "ReceivedAt"
            },
            values,
            "EventId",
            ct);
    }

    public Task<int> InsertWebSessionsAsync(IReadOnlyList<WebSessionRow> rows, CancellationToken ct)
    {
        var values = rows.Select(r => new object?[]
        {
            Guid.NewGuid(),
            r.SessionId,
            r.DeviceId,
            r.Domain,
            r.Title,
            r.Url,
            r.StartAt,
            r.EndAt
        }).ToList();

        return InsertRowsAsync(
            "WebSessions",
            new[] { "Id", "SessionId", "DeviceId", "Domain", "Title", "Url", "StartAt", "EndAt" },
            values,
            "SessionId",
            ct);
    }

    public Task<int> InsertAppSessionsAsync(IReadOnlyList<AppSessionRow> rows, CancellationToken ct)
    {
        var values = rows.Select(r => new object?[]
        {
            Guid.NewGuid(),
            r.SessionId,
            r.DeviceId,
            r.ProcessName,
            r.WindowTitle,
            r.StartAt,
            r.EndAt
        }).ToList();

        return InsertRowsAsync(
            "AppSessions",
            new[] { "Id", "SessionId", "DeviceId", "ProcessName", "WindowTitle", "StartAt", "EndAt" },
            values,
            "SessionId",
            ct);
    }

    public Task<int> InsertIdleSessionsAsync(IReadOnlyList<IdleSessionRow> rows, CancellationToken ct)
    {
        var values = rows.Select(r => new object?[]
        {
            Guid.NewGuid(),
            r.SessionId,
            r.DeviceId,
            r.StartAt,
            r.EndAt
        }).ToList();

        return InsertRowsAsync(
            "IdleSessions",
            new[] { "Id", "SessionId", "DeviceId", "StartAt", "EndAt" },
            values,
            "SessionId",
            ct);
    }

    public async Task EnsureDevicesAsync(
        IReadOnlyList<string> deviceIds,
        DateTimeOffset lastSeenAt,
        CancellationToken ct)
    {
        if (deviceIds.Count == 0)
        {
            return;
        }

        var existing = await _db.Devices
            .Where(d => deviceIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);

        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = deviceIds.Where(id => !existingSet.Contains(id)).Distinct().ToList();

        if (missing.Count > 0)
        {
            foreach (var id in missing)
            {
                _db.Devices.Add(new Device { Id = id, LastSeenAt = lastSeenAt });
            }
        }

        foreach (var id in existingSet)
        {
            var device = new Device { Id = id, LastSeenAt = lastSeenAt };
            _db.Devices.Attach(device);
            _db.Entry(device).Property(d => d.LastSeenAt).IsModified = true;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> InsertRowsAsync(
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows,
        string conflictColumn,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await _db.Database.OpenConnectionAsync(ct);
        }

        try
        {
            var totalInserted = 0;
            for (var offset = 0; offset < rows.Count; offset += IngestLimits.InsertChunkSize)
            {
                var chunk = rows.Skip(offset).Take(IngestLimits.InsertChunkSize).ToList();
                var sql = new StringBuilder();
                sql.Append("INSERT INTO \"").Append(tableName).Append("\" (");
                sql.Append(string.Join(", ", columns.Select(c => $"\"{c}\"")));
                sql.Append(") VALUES ");

                var parameters = new List<NpgsqlParameter>();
                var paramIndex = 0;
                for (var i = 0; i < chunk.Count; i++)
                {
                    if (i > 0)
                    {
                        sql.Append(", ");
                    }

                    sql.Append("(");
                    for (var j = 0; j < columns.Count; j++)
                    {
                        if (j > 0)
                        {
                            sql.Append(", ");
                        }

                        var paramName = "@p" + paramIndex++;
                        sql.Append(paramName);
                        parameters.Add(new NpgsqlParameter(paramName, chunk[i][j] ?? DBNull.Value));
                    }
                    sql.Append(")");
                }

                sql.Append(" ON CONFLICT (\"").Append(conflictColumn).Append("\") DO NOTHING;");

                await using var command = connection.CreateCommand();
                command.CommandText = sql.ToString();
                command.Parameters.AddRange(parameters.ToArray());

                totalInserted += await command.ExecuteNonQueryAsync(ct);
            }

            return totalInserted;
        }
        finally
        {
            if (shouldClose)
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }
}
