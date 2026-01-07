using System.Net.Http.Json;
using System.Net.Sockets;
using Agent.Service;
using Agent.Service.Identity;
using Agent.Service.Infrastructure;
using Agent.Service.Infrastructure.Outbox;
using Agent.Service.Tracking;
using Agent.Service.Workers;
using Agent.Shared.Abstractions;
using Agent.Shared.Config;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Tracker.Api.Data;
using Tracker.Api.Endpoints;
using Tracker.Api.Services;
using Xunit;

namespace EmployeeTracker.IntegrationTests;

public sealed class EndToEndIngestTests : IAsyncLifetime
{
    private const string LocalApiToken = "test-token";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("tracker")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplication? _apiApp;
    private IHost? _agentHost;
    private string? _apiBaseUrl;
    private int _localApiPort;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _apiBaseUrl = await StartTrackerApiAsync(_postgres.GetConnectionString());
        _localApiPort = GetFreePort();
        _agentHost = await StartAgentServiceAsync(_apiBaseUrl, _localApiPort);
    }

    public async Task DisposeAsync()
    {
        if (_agentHost is not null)
        {
            await _agentHost.StopAsync();
            _agentHost.Dispose();
        }

        if (_apiApp is not null)
        {
            await _apiApp.StopAsync();
            await _apiApp.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
#if RUN_DOCKER_TESTS
    [Fact]
#else
    [Fact(Skip = "Skipping Docker/Testcontainers tests. Set RUN_DOCKER_TESTS=1 to enable.")]
#endif
    public async Task EndToEndFlow_WritesSessionsToDatabase()
    {
        var testStart = DateTimeOffset.UtcNow;
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_localApiPort}")
        };
        client.DefaultRequestHeaders.Add("X-Agent-Token", LocalApiToken);

        await client.PostAsJsonAsync("/events/app-focus", new AppFocusRequest("Safari", "Docs", DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await client.PostAsJsonAsync("/events/app-focus", new AppFocusRequest("Finder", "Home", DateTimeOffset.UtcNow));

        await client.PostAsJsonAsync("/events/idle", new IdleSampleRequest(120, DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await client.PostAsJsonAsync("/events/idle", new IdleSampleRequest(0, DateTimeOffset.UtcNow));

        await WaitForOutboxFlushAsync(client, TimeSpan.FromSeconds(20));

        await WaitForConditionAsync(async () =>
        {
            using var scope = _apiApp!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

            var appSessions = await db.AppSessions.CountAsync(s => s.EndAt >= testStart);
            var idleSessions = await db.IdleSessions.CountAsync(s => s.EndAt >= testStart);

            return appSessions > 0 && idleSessions > 0;
        }, TimeSpan.FromSeconds(20));
    }

    private async Task<string> StartTrackerApiAsync(string connectionString)
    {
        var port = GetFreePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddDbContext<TrackerDbContext>(o =>
            o.UseNpgsql(connectionString).EnableSensitiveDataLogging());
        builder.Services.AddScoped<IIngestService, IngestService>();

        var app = builder.Build();
        app.MapIngestEndpoints();
        app.MapDevicesEndpoints();
        app.MapHealthEndpoints();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
            await db.Database.MigrateAsync();
        }

        await app.StartAsync();
        _apiApp = app;
        return $"http://127.0.0.1:{port}";
    }

    private static async Task<IHost> StartAgentServiceAsync(string apiBaseUrl, int localApiPort)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalApi:Token"] = LocalApiToken
        });
        builder.Services.Configure<AgentConfig>(options =>
        {
            options.EnableLocalCollectors = false;
            options.LocalApiPort = localApiPort;
            options.GlobalLocalApiToken = LocalApiToken;
            options.SenderIntervalSeconds = 1;
            options.WebEventSendMinSeconds = 1;
            options.WebEventSendMaxSeconds = 1;
            options.WebSessionSendMinSeconds = 1;
            options.WebSessionSendMaxSeconds = 1;
            options.AppSessionSendMinSeconds = 1;
            options.AppSessionSendMaxSeconds = 1;
            options.IdleSessionSendMinSeconds = 1;
            options.IdleSessionSendMaxSeconds = 1;
            options.WebEventBatchSize = 10;
            options.WebSessionBatchSize = 10;
            options.AppSessionBatchSize = 10;
            options.IdleSessionBatchSize = 10;
            options.WebEventIngestEndpoint = $"{apiBaseUrl}/events/web/batch";
            options.WebSessionIngestEndpoint = $"{apiBaseUrl}/ingest/web-sessions";
            options.AppSessionIngestEndpoint = $"{apiBaseUrl}/ingest/app-sessions";
            options.IdleSessionIngestEndpoint = $"{apiBaseUrl}/ingest/idle-sessions";
            options.DeviceHeartbeatEndpoint = $"{apiBaseUrl}/devices/heartbeat";
        });

        builder.Services.AddSingleton<DeviceIdentityStore>();
        builder.Services.AddSingleton<OutboxRepository>();
        builder.Services.AddSingleton<IOutboxService, OutboxService>();
        builder.Services.AddSingleton<OutboxSenderState>();
        builder.Services.AddSingleton<WebSessionizer>();
        builder.Services.AddSingleton<AppSessionizer>();
        builder.Services.AddSingleton<IdleSessionizer>();
        builder.Services.AddHttpClient("backend");
        builder.Services.AddHostedService<ShutdownFlushService>();
        builder.Services.AddHostedService<OutboxSenderWorker>();
        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        host.Services.GetRequiredService<OutboxRepository>().Init();
        await host.StartAsync();
        return host;
    }

    private static async Task WaitForOutboxFlushAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var health = await client.GetFromJsonAsync<LocalHealthResponse>("/health");
            if (health?.LastOutboxFlushAtUtc is not null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException("Timed out waiting for outbox flush.");
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException("Timed out waiting for test condition.");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record AppFocusRequest(string AppName, string? WindowTitle, DateTimeOffset TimestampUtc);
    private sealed record IdleSampleRequest(double IdleSeconds, DateTimeOffset TimestampUtc);
    private sealed record LocalHealthResponse(bool Ok, DateTimeOffset? LastOutboxFlushAtUtc);
}
