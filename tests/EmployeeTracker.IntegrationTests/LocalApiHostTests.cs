using System.Net;
using System.Net.Sockets;
using Agent.Service.Infrastructure;
using Agent.Service.Infrastructure.Outbox;
using Agent.Service.LocalApi;
using Agent.Service.Tracking;
using Agent.Shared.Config;
using Agent.Shared.LocalApi;
using Agent.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace EmployeeTracker.IntegrationTests;

[Collection("LocalApi")]
public sealed class LocalApiHostTests : IAsyncLifetime
{
    private const string Token = "test-token";
    private LocalApiHost? _localApi;
    private OutboxRepository? _outboxRepo;
    private LocalApiClient? _client;
    private HttpClient? _httpClient;
    private int _port;

    public async Task InitializeAsync()
    {
        _port = GetFreePort();

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var outboxLogger = loggerFactory.CreateLogger<OutboxRepository>();
        var webLogger = loggerFactory.CreateLogger<WebSessionizer>();
        var idleLogger = loggerFactory.CreateLogger<IdleSessionizer>();

        var config = Options.Create(new AgentConfig());
        _outboxRepo = new OutboxRepository(config, outboxLogger);
        _outboxRepo.Init();

        var outboxService = new OutboxService(_outboxRepo);
        var identityStore = new Agent.Service.Identity.DeviceIdentityStore();

        var webSessionizer = new WebSessionizer(outboxService, webLogger);
        var appSessionizer = new AppSessionizer(outboxService, identityStore);
        var idleSessionizer = new IdleSessionizer(outboxService, identityStore, idleLogger);

        var queue = new WebEventQueue();
        _localApi = new LocalApiHost(
            queue,
            Token,
            appSessionizer,
            idleSessionizer,
            webSessionizer,
            _outboxRepo,
            new OutboxSenderState(),
            "test-device",
            "test-version");
        _localApi.Port = _port;
        await _localApi.StartAsync(CancellationToken.None);

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _client = new LocalApiClient(_httpClient, $"http://127.0.0.1:{_port}", Token);
    }

    public async Task DisposeAsync()
    {
        if (_localApi is not null)
        {
            await _localApi.StopAsync(CancellationToken.None);
        }

        _httpClient?.Dispose();
    }

    [Fact]
    public async Task AuthSemantics_AreConsistent()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };

        var missingAuth = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.Unauthorized, missingAuth.StatusCode);

        var wrong = new HttpRequestMessage(HttpMethod.Get, "/health");
        wrong.Headers.Add(LocalApiConstants.AuthHeaderName, "wrong-token");
        var wrongAuth = await http.SendAsync(wrong);
        Assert.Equal(HttpStatusCode.Forbidden, wrongAuth.StatusCode);
    }

    [Fact]
    public async Task LocalApiContract_ReturnsExpectedPayloads()
    {
        var version = await _client!.GetVersionAsync(CancellationToken.None);
        Assert.True(version.Success);
        Assert.Equal(LocalApiConstants.Contract, version.Value?.Contract);

        var health = await _client.GetHealthAsync(CancellationToken.None);
        Assert.True(health.Success);
        Assert.True(health.Value?.Ok ?? false);

        var diag = await _client.GetDiagAsync(CancellationToken.None);
        Assert.True(diag.Success);
        Assert.Equal(LocalApiConstants.Contract, diag.Value?.Contract);
        Assert.Equal(_port, diag.Value?.Port);
        Assert.True(diag.Value?.Outbox.Available ?? false);
    }

    [Fact]
    public async Task LocalApi_AcceptsIdleAndAppFocus()
    {
        var idleStart = await _client!.PostIdleAsync(new IdleSampleRequest(120, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(idleStart.Success);
        var idleEnd = await _client.PostIdleAsync(new IdleSampleRequest(0, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(idleEnd.Success);

        var appFocus = await _client.PostAppFocusAsync(
            new AppFocusRequest("Explorer", "Test", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(appFocus.Success);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
