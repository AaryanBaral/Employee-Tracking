using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;
using Tracker.Api.Endpoints;
using Tracker.Api.Middleware;
using Tracker.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});
builder.Services.AddDbContext<TrackerDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")).EnableSensitiveDataLogging());
builder.Services.AddCors(options =>
{
    options.AddPolicy("ui", policy =>
    {
        policy.WithOrigins("http://localhost:8080", "http://192.168.0.6:8080", "http://localhost:5000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddScoped<IIngestService, IngestService>();

var app = builder.Build();

app.UseCors("ui");
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/", () => "Tracker API");

app.MapIngestEndpoints();
app.MapDevicesEndpoints();
app.MapHealthEndpoints();

app.Run();
