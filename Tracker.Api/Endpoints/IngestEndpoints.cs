using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Contracts;
using Tracker.Api.Data;
using Tracker.Api.Infrastructure;
using Tracker.Api.Models;
using Tracker.Api.Services;
using Tracker.Api.Services.Models;

namespace Tracker.Api.Endpoints;

public static class IngestEndpoints
{
    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ingest/web-sessions", IngestWebSessionsAsync);
        app.MapPost("/ingest/app-sessions", IngestAppSessionsAsync);
        app.MapPost("/ingest/idle-sessions", IngestIdleSessionsAsync);
        app.MapPost("/events/web", IngestWebEventAsync);
        app.MapPost("/events/web/batch", IngestWebEventBatchAsync);
        return app;
    }

    private static async Task<IResult> IngestWebSessionsAsync(
        WebSessionIngestRequest req,
        TrackerDbContext db,
        IIngestService ingestService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var deviceId = req.DeviceId?.Trim();
        var agentVersion = req.AgentVersion?.Trim();
        if (req.BatchId == Guid.Empty)
        {
            return Results.BadRequest("batchId is required.");
        }
        if (req.Sequence < 0)
        {
            return Results.BadRequest("sequence must be >= 0.");
        }
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Results.BadRequest("deviceId is required.");
        }
        if (string.IsNullOrWhiteSpace(agentVersion))
        {
            return Results.BadRequest("agentVersion is required.");
        }
        if (agentVersion.Length > 64)
        {
            return Results.BadRequest("agentVersion is too long.");
        }

        if (req.Sessions is null || req.Sessions.Count == 0)
        {
            return Results.BadRequest("sessions are required.");
        }
        if (req.Sessions.Count > IngestLimits.MaxSessionsPerRequest)
        {
            return Results.BadRequest($"sessions exceeds max batch size of {IngestLimits.MaxSessionsPerRequest}.");
        }

        var now = DateTimeOffset.UtcNow;
        if (req.SentAt > now.AddDays(1))
        {
            return Results.BadRequest("sentAt is too far in the future.");
        }

        logger.LogInformation(
            "Web ingest started for device {deviceId} with {count} sessions. BatchId={batchId} Sequence={sequence}.",
            deviceId,
            req.Sessions.Count,
            req.BatchId,
            req.Sequence);

        try
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is null)
            {
                logger.LogInformation("Registering new device {deviceId}.", deviceId);
                device = new Device { Id = deviceId, LastSeenAt = DateTimeOffset.UtcNow };
                db.Devices.Add(device);
                await db.SaveChangesAsync(ct);
            }

            var cursor = await db.IngestCursors.FirstOrDefaultAsync(
                c => c.DeviceId == deviceId && c.Stream == "web",
                ct);
            if (cursor is not null && req.Sequence <= cursor.LastSequence)
            {
                logger.LogInformation(
                    "Web ingest already processed for device {deviceId}. Sequence={sequence} LastSequence={lastSequence}.",
                    deviceId,
                    req.Sequence,
                    cursor.LastSequence);
                return Results.Ok(new IngestResponse(req.Sessions.Count, 0, 0, req.Sessions.Count));
            }

            var rows = new List<WebSessionRow>(req.Sessions.Count);
            var skipped = 0;
            var maxFuture = now.AddDays(1);
            foreach (var s in req.Sessions)
            {
                if (s.SessionId == Guid.Empty)
                {
                    skipped++;
                    logger.LogWarning("Skipping web session with empty sessionId for {deviceId}.", deviceId);
                    continue;
                }

                if (s.EndAt <= s.StartAt || s.StartAt > maxFuture || s.EndAt > maxFuture)
                {
                    skipped++;
                    logger.LogWarning("Skipping invalid web session for {deviceId}.", deviceId);
                    continue;
                }

                var domain = s.Domain.Trim();
                if (string.IsNullOrWhiteSpace(domain))
                {
                    skipped++;
                    logger.LogWarning("Skipping web session with empty domain for {deviceId}.", deviceId);
                    continue;
                }

                if (domain.Length > 255)
                {
                    domain = domain[..255];
                }

                var title = s.Title?.Trim();
                if (!string.IsNullOrEmpty(title) && title.Length > 512)
                {
                    title = title[..512];
                }

                var url = s.Url?.Trim();
                if (!string.IsNullOrEmpty(url) && url.Length > 2048)
                {
                    url = url[..2048];
                }

                rows.Add(new WebSessionRow(
                    s.SessionId,
                    deviceId,
                    domain,
                    title,
                    url,
                    s.StartAt,
                    s.EndAt));
            }

            if (rows.Count == 0)
            {
                logger.LogInformation("No valid web sessions to insert for {deviceId}.", deviceId);
                return Results.Ok(new IngestResponse(req.Sessions.Count, skipped, 0, 0));
            }

            var inserted = await ingestService.InsertWebSessionsAsync(rows, ct);
            var duplicates = rows.Count - inserted;
            device.LastSeenAt = DateTimeOffset.UtcNow;

            if (cursor is null)
            {
                cursor = new IngestCursor
                {
                    DeviceId = deviceId,
                    Stream = "web",
                    LastSequence = req.Sequence,
                    LastBatchId = req.BatchId,
                    LastSentAt = req.SentAt
                };
                db.IngestCursors.Add(cursor);
            }
            else if (req.Sequence > cursor.LastSequence)
            {
                cursor.LastSequence = req.Sequence;
                cursor.LastBatchId = req.BatchId;
                cursor.LastSentAt = req.SentAt;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Inserted {count} web sessions for {deviceId}. Skipped={skipped}. Duplicates={duplicates}. DurationMs={durationMs}.",
                inserted,
                deviceId,
                skipped,
                duplicates,
                sw.ElapsedMilliseconds);
            return Results.Ok(new IngestResponse(req.Sessions.Count, skipped, inserted, duplicates));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest web sessions for {deviceId}.", deviceId);
            return Results.Problem("Failed to ingest web sessions.");
        }
    }

    private static async Task<IResult> IngestWebEventAsync(
        WebEventIngestRequest req,
        IIngestService ingestService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (req.EventId == Guid.Empty)
        {
            return Results.BadRequest("eventId is required.");
        }

        var deviceId = req.DeviceId?.Trim();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Results.BadRequest("deviceId is required.");
        }
        var agentVersion = req.AgentVersion?.Trim();
        if (string.IsNullOrWhiteSpace(agentVersion))
        {
            return Results.BadRequest("agentVersion is required.");
        }
        if (agentVersion.Length > 64)
        {
            return Results.BadRequest("agentVersion is too long.");
        }
        if (req.BatchId == Guid.Empty)
        {
            return Results.BadRequest("batchId is required.");
        }
        if (req.Sequence < 0)
        {
            return Results.BadRequest("sequence must be >= 0.");
        }

        var domain = req.Domain?.Trim();
        if (string.IsNullOrWhiteSpace(domain))
        {
            return Results.BadRequest("domain is required.");
        }

        if (req.Timestamp > now.AddDays(2))
        {
            return Results.BadRequest("timestamp is too far in the future.");
        }
        if (req.SentAt > now.AddDays(1))
        {
            return Results.BadRequest("sentAt is too far in the future.");
        }

        if (deviceId.Length > 64)
        {
            return Results.BadRequest("deviceId is too long.");
        }

        if (domain.Length > 255)
        {
            domain = domain[..255];
        }

        var title = req.Title?.Trim();
        if (!string.IsNullOrEmpty(title) && title.Length > 512)
        {
            title = title[..512];
        }

        var url = req.Url?.Trim();
        if (!string.IsNullOrEmpty(url) && url.Length > 2048)
        {
            url = url[..2048];
        }

        var browser = req.Browser?.Trim();
        if (!string.IsNullOrEmpty(browser) && browser.Length > 64)
        {
            browser = browser[..64];
        }

        try
        {
            var row = new WebEventRow(
                req.EventId,
                deviceId,
                domain,
                title,
                url,
                req.Timestamp,
                browser,
                now);

            await ingestService.EnsureDevicesAsync(new[] { deviceId }, now, ct);
            var inserted = await ingestService.InsertWebEventsAsync(new[] { row }, ct);
            var duplicates = inserted == 0 ? 1 : 0;

            logger.LogInformation(
                "Web event ingest for {deviceId}. Inserted={inserted}.",
                deviceId,
                inserted);

            return Results.Ok(new IngestResponse(1, 0, inserted, duplicates));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest web event for {deviceId}.", deviceId);
            return Results.Problem("Failed to ingest web event.");
        }
    }

    private static async Task<IResult> IngestWebEventBatchAsync(
        WebEventBatchRequest request,
        IIngestService ingestService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (request.BatchId == Guid.Empty)
        {
            return Results.BadRequest("batchId is required.");
        }
        if (request.Sequence < 0)
        {
            return Results.BadRequest("sequence must be >= 0.");
        }

        var deviceId = request.DeviceId?.Trim();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Results.BadRequest("deviceId is required.");
        }
        if (deviceId.Length > 64)
        {
            return Results.BadRequest("deviceId is too long.");
        }

        var agentVersion = request.AgentVersion?.Trim();
        if (string.IsNullOrWhiteSpace(agentVersion))
        {
            return Results.BadRequest("agentVersion is required.");
        }
        if (agentVersion.Length > 64)
        {
            return Results.BadRequest("agentVersion is too long.");
        }

        if (request.Events is null || request.Events.Count == 0)
        {
            return Results.BadRequest("events are required.");
        }

        if (request.Events.Count > IngestLimits.MaxWebEventsPerRequest)
        {
            return Results.BadRequest($"events exceeds max batch size of {IngestLimits.MaxWebEventsPerRequest}.");
        }

        var now = DateTimeOffset.UtcNow;
        if (request.SentAt > now.AddDays(1))
        {
            return Results.BadRequest("sentAt is too far in the future.");
        }
        var maxFuture = now.AddDays(2);
        var rows = new List<WebEventRow>(request.Events.Count);
        var invalid = 0;

        foreach (var evt in request.Events)
        {
            if (evt.EventId == Guid.Empty)
            {
                invalid++;
                continue;
            }

            var domain = evt.Domain?.Trim();
            if (string.IsNullOrWhiteSpace(domain))
            {
                invalid++;
                continue;
            }

            if (evt.Timestamp > maxFuture)
            {
                invalid++;
                continue;
            }

            if (domain.Length > 255)
            {
                domain = domain[..255];
            }

            var title = evt.Title?.Trim();
            if (!string.IsNullOrEmpty(title) && title.Length > 512)
            {
                title = title[..512];
            }

            var url = evt.Url?.Trim();
            if (!string.IsNullOrEmpty(url) && url.Length > 2048)
            {
                url = url[..2048];
            }

            var browser = evt.Browser?.Trim();
            if (!string.IsNullOrEmpty(browser) && browser.Length > 64)
            {
                browser = browser[..64];
            }

            rows.Add(new WebEventRow(
                evt.EventId,
                deviceId,
                domain,
                title,
                url,
                evt.Timestamp,
                browser,
                now));
        }

        if (rows.Count == 0)
        {
            return Results.Ok(new IngestResponse(request.Events.Count, invalid, 0, 0));
        }

        try
        {
            await ingestService.EnsureDevicesAsync(new[] { deviceId }, now, ct);
            var inserted = await ingestService.InsertWebEventsAsync(rows, ct);
            var duplicates = rows.Count - inserted;

            logger.LogInformation(
                "Web event batch ingest completed. Received={received} Inserted={inserted} Invalid={invalid} Duplicates={duplicates}.",
                request.Events.Count,
                inserted,
                invalid,
                duplicates);

            return Results.Ok(new IngestResponse(request.Events.Count, invalid, inserted, duplicates));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest web event batch.");
            return Results.Problem("Failed to ingest web events.");
        }
    }

    private static async Task<IResult> IngestAppSessionsAsync(
        AppSessionIngestRequest req,
        TrackerDbContext db,
        IIngestService ingestService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var deviceId = req.DeviceId?.Trim();
        var agentVersion = req.AgentVersion?.Trim();
        if (req.BatchId == Guid.Empty)
        {
            return Results.BadRequest("batchId is required.");
        }
        if (req.Sequence < 0)
        {
            return Results.BadRequest("sequence must be >= 0.");
        }
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Results.BadRequest("deviceId is required.");
        }
        if (string.IsNullOrWhiteSpace(agentVersion))
        {
            return Results.BadRequest("agentVersion is required.");
        }
        if (agentVersion.Length > 64)
        {
            return Results.BadRequest("agentVersion is too long.");
        }

        if (req.Sessions is null || req.Sessions.Count == 0)
        {
            return Results.BadRequest("sessions are required.");
        }

        var now = DateTimeOffset.UtcNow;
        if (req.SentAt > now.AddDays(1))
        {
            return Results.BadRequest("sentAt is too far in the future.");
        }

        logger.LogInformation(
            "App ingest started for device {deviceId} with {count} sessions. BatchId={batchId} Sequence={sequence}.",
            deviceId,
            req.Sessions.Count,
            req.BatchId,
            req.Sequence);

        try
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is null)
            {
                logger.LogInformation("Registering new device {deviceId}.", deviceId);
                device = new Device { Id = deviceId, LastSeenAt = DateTimeOffset.UtcNow };
                db.Devices.Add(device);
                await db.SaveChangesAsync(ct);
            }

            var cursor = await db.IngestCursors.FirstOrDefaultAsync(
                c => c.DeviceId == deviceId && c.Stream == "app",
                ct);
            if (cursor is not null && req.Sequence <= cursor.LastSequence)
            {
                logger.LogInformation(
                    "App ingest already processed for device {deviceId}. Sequence={sequence} LastSequence={lastSequence}.",
                    deviceId,
                    req.Sequence,
                    cursor.LastSequence);
                return Results.Ok(new IngestResponse(req.Sessions.Count, 0, 0, req.Sessions.Count));
            }

            var rows = new List<AppSessionRow>(req.Sessions.Count);
            var skipped = 0;
            var maxFuture = now.AddDays(1);
            foreach (var s in req.Sessions)
            {
                if (s.SessionId == Guid.Empty)
                {
                    skipped++;
                    logger.LogWarning("Skipping app session with empty sessionId for {deviceId}.", deviceId);
                    continue;
                }

                if (s.EndAt <= s.StartAt || s.StartAt > maxFuture || s.EndAt > maxFuture)
                {
                    skipped++;
                    logger.LogWarning("Skipping invalid app session for {deviceId}.", deviceId);
                    continue;
                }

                var processName = s.ProcessName.Trim();
                if (string.IsNullOrWhiteSpace(processName))
                {
                    skipped++;
                    logger.LogWarning("Skipping app session with empty process for {deviceId}.", deviceId);
                    continue;
                }

                if (processName.Length > 255)
                {
                    processName = processName[..255];
                }

                var windowTitle = s.WindowTitle?.Trim();
                if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Length > 512)
                {
                    windowTitle = windowTitle[..512];
                }

                rows.Add(new AppSessionRow(
                    s.SessionId,
                    deviceId,
                    processName,
                    windowTitle,
                    s.StartAt,
                    s.EndAt));
            }

            if (rows.Count == 0)
            {
                logger.LogInformation("No valid app sessions to insert for {deviceId}.", deviceId);
                return Results.Ok(new IngestResponse(req.Sessions.Count, skipped, 0, 0));
            }

            var inserted = await ingestService.InsertAppSessionsAsync(rows, ct);
            var duplicates = rows.Count - inserted;
            device.LastSeenAt = DateTimeOffset.UtcNow;

            if (cursor is null)
            {
                cursor = new IngestCursor
                {
                    DeviceId = deviceId,
                    Stream = "app",
                    LastSequence = req.Sequence,
                    LastBatchId = req.BatchId,
                    LastSentAt = req.SentAt
                };
                db.IngestCursors.Add(cursor);
            }
            else if (req.Sequence > cursor.LastSequence)
            {
                cursor.LastSequence = req.Sequence;
                cursor.LastBatchId = req.BatchId;
                cursor.LastSentAt = req.SentAt;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Inserted {count} app sessions for {deviceId}. Skipped={skipped}. Duplicates={duplicates}. DurationMs={durationMs}.",
                inserted,
                deviceId,
                skipped,
                duplicates,
                sw.ElapsedMilliseconds);
            return Results.Ok(new IngestResponse(req.Sessions.Count, skipped, inserted, duplicates));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest app sessions for {deviceId}.", deviceId);
            return Results.Problem("Failed to ingest app sessions.");
        }
    }

    private static async Task<IResult> IngestIdleSessionsAsync(
        IdleSessionIngestRequest req,
        TrackerDbContext db,
        IIngestService ingestService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var deviceId = req.DeviceId?.Trim();
        var agentVersion = req.AgentVersion?.Trim();
        if (req.BatchId == Guid.Empty)
        {
            return Results.BadRequest("batchId is required.");
        }
        if (req.Sequence < 0)
        {
            return Results.BadRequest("sequence must be >= 0.");
        }
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Results.BadRequest("deviceId is required.");
        }
        if (string.IsNullOrWhiteSpace(agentVersion))
        {
            return Results.BadRequest("agentVersion is required.");
        }
        if (agentVersion.Length > 64)
        {
            return Results.BadRequest("agentVersion is too long.");
        }

        if (req.Sessions is null || req.Sessions.Count == 0)
        {
            return Results.BadRequest("sessions are required.");
        }

        var now = DateTimeOffset.UtcNow;
        if (req.SentAt > now.AddDays(1))
        {
            return Results.BadRequest("sentAt is too far in the future.");
        }

        logger.LogInformation(
            "Idle ingest started for device {deviceId} with {count} sessions. BatchId={batchId} Sequence={sequence}.",
            deviceId,
            req.Sessions.Count,
            req.BatchId,
            req.Sequence);

        try
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is null)
            {
                logger.LogInformation("Registering new device {deviceId}.", deviceId);
                device = new Device { Id = deviceId, LastSeenAt = DateTimeOffset.UtcNow };
                db.Devices.Add(device);
                await db.SaveChangesAsync(ct);
            }

            var cursor = await db.IngestCursors.FirstOrDefaultAsync(
                c => c.DeviceId == deviceId && c.Stream == "idle",
                ct);
            if (cursor is not null && req.Sequence <= cursor.LastSequence)
            {
                logger.LogInformation(
                    "Idle ingest already processed for device {deviceId}. Sequence={sequence} LastSequence={lastSequence}.",
                    deviceId,
                    req.Sequence,
                    cursor.LastSequence);
                return Results.Ok(new IngestResponse(req.Sessions.Count, 0, 0, req.Sessions.Count));
            }

            var rows = new List<IdleSessionRow>(req.Sessions.Count);
            var skipped = 0;
            var maxFuture = now.AddDays(1);
            foreach (var s in req.Sessions)
            {
                if (s.SessionId == Guid.Empty)
                {
                    skipped++;
                    logger.LogWarning("Skipping idle session with empty sessionId for {deviceId}.", deviceId);
                    continue;
                }

                if (s.EndAt <= s.StartAt || s.StartAt > maxFuture || s.EndAt > maxFuture)
                {
                    skipped++;
                    logger.LogWarning("Skipping invalid idle session for {deviceId}.", deviceId);
                    continue;
                }

                rows.Add(new IdleSessionRow(
                    s.SessionId,
                    deviceId,
                    s.StartAt,
                    s.EndAt));
            }

            if (rows.Count == 0)
            {
                logger.LogInformation("No valid idle sessions to insert for {deviceId}.", deviceId);
                return Results.Ok(new IngestResponse(req.Sessions.Count, skipped, 0, 0));
            }

            var inserted = await ingestService.InsertIdleSessionsAsync(rows, ct);
            var duplicates = rows.Count - inserted;
            device.LastSeenAt = DateTimeOffset.UtcNow;

            if (cursor is null)
            {
                cursor = new IngestCursor
                {
                    DeviceId = deviceId,
                    Stream = "idle",
                    LastSequence = req.Sequence,
                    LastBatchId = req.BatchId,
                    LastSentAt = req.SentAt
                };
                db.IngestCursors.Add(cursor);
            }
            else if (req.Sequence > cursor.LastSequence)
            {
                cursor.LastSequence = req.Sequence;
                cursor.LastBatchId = req.BatchId;
                cursor.LastSentAt = req.SentAt;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Inserted {count} idle sessions for {deviceId}. Skipped={skipped}. Duplicates={duplicates}. DurationMs={durationMs}.",
                inserted,
                deviceId,
                skipped,
                duplicates,
                sw.ElapsedMilliseconds);
            return Results.Ok(new IngestResponse(req.Sessions.Count, skipped, inserted, duplicates));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest idle sessions for {deviceId}.", deviceId);
            return Results.Problem("Failed to ingest idle sessions.");
        }
    }
}
