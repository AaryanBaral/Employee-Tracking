using Tracker.Api.Data;

namespace Tracker.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", ReadyAsync);
        return app;
    }

    private static async Task<IResult> ReadyAsync(TrackerDbContext db, CancellationToken ct)
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        return canConnect
            ? Results.Ok(new { status = "ready" })
            : Results.Problem("Database not reachable.");
    }
}
