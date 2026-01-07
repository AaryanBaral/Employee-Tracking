using Microsoft.EntityFrameworkCore;
using Tracker.Api.Contracts;
using Tracker.Api.Data;

namespace Tracker.Api.Endpoints;

public static class DevicesEndpoints
{
    public static IEndpointRouteBuilder MapDevicesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/devices/heartbeat", HeartbeatAsync);
        app.MapPatch("/devices/{deviceId}", UpdateDeviceAsync);
        app.MapGet("/devices", GetDevicesAsync);
        app.MapGet("/devices/{deviceId}/summary", GetDeviceSummaryAsync);
        app.MapGet("/devices/{deviceId}/web-sessions", GetDeviceWebSessionsAsync);
        app.MapGet("/devices/{deviceId}/app-sessions", GetDeviceAppSessionsAsync);
        app.MapGet("/devices/{deviceId}/idle-sessions", GetDeviceIdleSessionsAsync);
        return app;
    }

    private static async Task<IResult> HeartbeatAsync(
        DeviceHeartbeatRequest request,
        TrackerDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var deviceId = request.DeviceId?.Trim();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Results.BadRequest("deviceId is required.");
        }

        var now = DateTimeOffset.UtcNow;
        if (request.LastSeenAt > now.AddDays(1))
        {
            return Results.BadRequest("lastSeenAt is too far in the future.");
        }

        try
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);

            if (device is null)
            {
                logger.LogInformation("Registering new device {deviceId} via heartbeat.", deviceId);
                device = new Models.Device
                {
                    Id = deviceId,
                    Hostname = request.Hostname?.Trim(),
                    LastSeenAt = request.LastSeenAt
                };
                db.Devices.Add(device);
            }
            else
            {
                device.LastSeenAt = request.LastSeenAt;
                if (!string.IsNullOrWhiteSpace(request.Hostname))
                    device.Hostname = request.Hostname.Trim();
            }

            var changes = await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Heartbeat upserted for {deviceId}. SaveChanges={changes}.",
                deviceId,
                changes);
            return Results.Ok(new { upserted = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upsert heartbeat for {deviceId}.", deviceId);
            return Results.Problem("Failed to process heartbeat.");
        }
    }

    private static async Task<IResult> UpdateDeviceAsync(
        string deviceId,
        UpdateDeviceRequest request,
        TrackerDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        try
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is null)
            {
                logger.LogWarning("Device {deviceId} not found for update.", deviceId);
                return Results.NotFound();
            }

            device.DisplayName = request.DisplayName?.Trim();
            if (request.IsSeen.HasValue)
            {
                device.LastReviewedAt = request.IsSeen.Value ? DateTimeOffset.UtcNow : null;
            }
            var changes = await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Device {deviceId} display name updated. SaveChanges={changes}.",
                deviceId,
                changes);
            return Results.Ok(new { updated = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update device {deviceId}.", deviceId);
            return Results.Problem("Failed to update device.");
        }
    }

    private static async Task<IResult> GetDevicesAsync(
        TrackerDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 50,
        string? query = null,
        string? search = null,
        bool? seen = null,
        DateTimeOffset? activeSince = null,
        CancellationToken ct = default)
    {
        try
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

            var deviceQuery = db.Devices.AsNoTracking();
            var term = !string.IsNullOrWhiteSpace(query) ? query.Trim() : search?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
            {
                var pattern = $"%{term}%";
                deviceQuery = deviceQuery.Where(d =>
                    EF.Functions.ILike(d.Id, pattern) ||
                    (d.Hostname != null && EF.Functions.ILike(d.Hostname, pattern)) ||
                    (d.DisplayName != null && EF.Functions.ILike(d.DisplayName, pattern)));
            }

            if (activeSince.HasValue)
            {
                deviceQuery = deviceQuery.Where(d => d.LastSeenAt >= activeSince.Value);
            }

            if (seen.HasValue)
            {
                deviceQuery = seen.Value
                    ? deviceQuery.Where(d => d.LastReviewedAt != null)
                    : deviceQuery.Where(d => d.LastReviewedAt == null);
            }

            var devices = await deviceQuery
                .OrderByDescending(d => d.LastSeenAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new DeviceListItem(
                    d.Id,
                    d.Hostname,
                    d.DisplayName,
                    d.LastSeenAt,
                    d.LastReviewedAt))
                .ToListAsync(ct);

            logger.LogInformation(
                "Fetched {count} devices. Page={page} PageSize={pageSize}.",
                devices.Count,
                page,
                pageSize);
            return Results.Ok(devices);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch devices.");
            return Results.Problem("Failed to fetch devices.");
        }
    }

    private static async Task<IResult> GetDeviceSummaryAsync(
        string deviceId,
        DateOnly date,
        TrackerDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        try
        {
            var trimmedDeviceId = deviceId.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDeviceId))
            {
                return Results.BadRequest("deviceId is required.");
            }

            var start = DateTimeOffset.Parse($"{date:yyyy-MM-dd}T00:00:00Z");
            var end = start.AddDays(1);

            var device = await db.Devices
                .AsNoTracking()
                .Where(d => d.Id == trimmedDeviceId)
                .Select(d => new DeviceSummaryDevice(
                    d.Id,
                    d.Hostname,
                    d.DisplayName,
                    d.LastSeenAt,
                    d.LastReviewedAt))
                .FirstOrDefaultAsync(ct);

            if (device is null)
            {
                return Results.NotFound(new { message = "Device not found" });
            }

            var idleSeconds = await db.IdleSecondsRows
                .FromSqlInterpolated($"""
                    SELECT COALESCE(SUM(EXTRACT(EPOCH FROM
                        (LEAST("EndAt", {end}) - GREATEST("StartAt", {start})))), 0)::bigint AS "Seconds"
                    FROM "IdleSessions"
                    WHERE "DeviceId" = {trimmedDeviceId} AND "StartAt" < {end} AND "EndAt" > {start}
                    """)
                .Select(x => x.Seconds)
                .FirstOrDefaultAsync(ct);

            var topDomains = await db.DomainSummaryRows
                .FromSqlInterpolated($"""
                    SELECT "Domain" AS "Domain",
                           COALESCE(SUM(EXTRACT(EPOCH FROM
                               (LEAST("EndAt", {end}) - GREATEST("StartAt", {start})))), 0)::bigint AS "Seconds"
                    FROM "WebSessions"
                    WHERE "DeviceId" = {trimmedDeviceId} AND "StartAt" < {end} AND "EndAt" > {start}
                    GROUP BY "Domain"
                    ORDER BY "Seconds" DESC
                    LIMIT 10
                    """)
                .Select(x => new DeviceSummaryItem(x.Domain, x.Seconds))
                .ToListAsync(ct);

            var topUrls = await db.UrlSummaryRows
                .FromSqlInterpolated($"""
                    SELECT COALESCE("Url", '') AS "Url",
                           COALESCE(SUM(EXTRACT(EPOCH FROM
                               (LEAST("EndAt", {end}) - GREATEST("StartAt", {start})))), 0)::bigint AS "Seconds"
                    FROM "WebSessions"
                    WHERE "DeviceId" = {trimmedDeviceId} AND "StartAt" < {end} AND "EndAt" > {start}
                    GROUP BY "Url"
                    ORDER BY "Seconds" DESC
                    LIMIT 10
                    """)
                .Select(x => new DeviceSummaryItem(x.Url, x.Seconds))
                .ToListAsync(ct);

            var topApps = await db.AppSummaryRows
                .FromSqlInterpolated($"""
                    SELECT "ProcessName" AS "ProcessName",
                           COALESCE(SUM(EXTRACT(EPOCH FROM
                               (LEAST("EndAt", {end}) - GREATEST("StartAt", {start})))), 0)::bigint AS "Seconds"
                    FROM "AppSessions"
                    WHERE "DeviceId" = {trimmedDeviceId} AND "StartAt" < {end} AND "EndAt" > {start}
                    GROUP BY "ProcessName"
                    ORDER BY "Seconds" DESC
                    LIMIT 10
                    """)
                .Select(x => new DeviceSummaryItem(x.ProcessName, x.Seconds))
                .ToListAsync(ct);

            logger.LogInformation(
                "Summary computed for {deviceId} on {date}.",
                trimmedDeviceId,
                date.ToString("yyyy-MM-dd"));

            return Results.Ok(new DeviceSummaryResponse(
                date.ToString("yyyy-MM-dd"),
                device,
                idleSeconds,
                topDomains,
                topUrls,
                topApps));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build summary for {deviceId}.", deviceId);
            return Results.Problem("Failed to build device summary.");
        }
    }

    private static async Task<IResult> GetDeviceWebSessionsAsync(
        string deviceId,
        TrackerDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 50,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string? search = null,
        CancellationToken ct = default)
    {
        try
        {
            var trimmedDeviceId = deviceId.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDeviceId))
            {
                return Results.BadRequest("deviceId is required.");
            }

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

            if (start.HasValue && end.HasValue && start >= end)
            {
                return Results.BadRequest("start must be before end.");
            }

            var query = db.WebSessions.AsNoTracking().Where(s => s.DeviceId == trimmedDeviceId);
            if (start.HasValue && end.HasValue)
            {
                query = query.Where(s => s.StartAt < end && s.EndAt > start);
            }
            else if (start.HasValue)
            {
                query = query.Where(s => s.EndAt >= start);
            }
            else if (end.HasValue)
            {
                query = query.Where(s => s.StartAt <= end);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var pattern = $"%{term}%";
                query = query.Where(s =>
                    EF.Functions.ILike(s.Domain, pattern) ||
                    (s.Title != null && EF.Functions.ILike(s.Title, pattern)) ||
                    (s.Url != null && EF.Functions.ILike(s.Url, pattern)));
            }

            var sessions = await query
                .OrderByDescending(s => s.StartAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new WebSessionListItem(
                    s.SessionId,
                    s.Domain,
                    s.Title,
                    s.Url,
                    s.StartAt,
                    s.EndAt))
                .ToListAsync(ct);

            logger.LogInformation(
                "Fetched {count} web sessions for {deviceId}. Page={page} PageSize={pageSize}.",
                sessions.Count,
                trimmedDeviceId,
                page,
                pageSize);

            return Results.Ok(sessions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch web sessions for {deviceId}.", deviceId);
            return Results.Problem("Failed to fetch web sessions.");
        }
    }

    private static async Task<IResult> GetDeviceAppSessionsAsync(
        string deviceId,
        TrackerDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 50,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string? search = null,
        CancellationToken ct = default)
    {
        try
        {
            var trimmedDeviceId = deviceId.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDeviceId))
            {
                return Results.BadRequest("deviceId is required.");
            }

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

            if (start.HasValue && end.HasValue && start >= end)
            {
                return Results.BadRequest("start must be before end.");
            }

            var query = db.AppSessions.AsNoTracking().Where(s => s.DeviceId == trimmedDeviceId);
            if (start.HasValue && end.HasValue)
            {
                query = query.Where(s => s.StartAt < end && s.EndAt > start);
            }
            else if (start.HasValue)
            {
                query = query.Where(s => s.EndAt >= start);
            }
            else if (end.HasValue)
            {
                query = query.Where(s => s.StartAt <= end);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var pattern = $"%{term}%";
                query = query.Where(s =>
                    EF.Functions.ILike(s.ProcessName, pattern) ||
                    (s.WindowTitle != null && EF.Functions.ILike(s.WindowTitle, pattern)));
            }

            var sessions = await query
                .OrderByDescending(s => s.StartAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new AppSessionListItem(
                    s.SessionId,
                    s.ProcessName,
                    s.WindowTitle,
                    s.StartAt,
                    s.EndAt))
                .ToListAsync(ct);

            logger.LogInformation(
                "Fetched {count} app sessions for {deviceId}. Page={page} PageSize={pageSize}.",
                sessions.Count,
                trimmedDeviceId,
                page,
                pageSize);

            return Results.Ok(sessions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch app sessions for {deviceId}.", deviceId);
            return Results.Problem("Failed to fetch app sessions.");
        }
    }

    private static async Task<IResult> GetDeviceIdleSessionsAsync(
        string deviceId,
        TrackerDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 50,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken ct = default)
    {
        try
        {
            var trimmedDeviceId = deviceId.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDeviceId))
            {
                return Results.BadRequest("deviceId is required.");
            }

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

            if (start.HasValue && end.HasValue && start >= end)
            {
                return Results.BadRequest("start must be before end.");
            }

            var query = db.IdleSessions.AsNoTracking().Where(s => s.DeviceId == trimmedDeviceId);
            if (start.HasValue && end.HasValue)
            {
                query = query.Where(s => s.StartAt < end && s.EndAt > start);
            }
            else if (start.HasValue)
            {
                query = query.Where(s => s.EndAt >= start);
            }
            else if (end.HasValue)
            {
                query = query.Where(s => s.StartAt <= end);
            }

            var sessions = await query
                .OrderByDescending(s => s.StartAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new IdleSessionListItem(
                    s.SessionId,
                    s.StartAt,
                    s.EndAt))
                .ToListAsync(ct);

            logger.LogInformation(
                "Fetched {count} idle sessions for {deviceId}. Page={page} PageSize={pageSize}.",
                sessions.Count,
                trimmedDeviceId,
                page,
                pageSize);

            return Results.Ok(sessions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch idle sessions for {deviceId}.", deviceId);
            return Results.Problem("Failed to fetch idle sessions.");
        }
    }
}
