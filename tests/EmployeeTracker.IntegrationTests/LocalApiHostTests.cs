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
    private WebEventQueue? _queue;
    private Task? _consumer;
    private CancellationTokenSource? _consumerCts;
    private ILoggerFactory? _loggerFactory;
    private int _port;
    private WebSessionizer? _webSessionizer;
    private string _deviceId = "test-device";

    public async Task InitializeAsync()
    {
        _port = GetFreePort();

        _loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var outboxLogger = _loggerFactory.CreateLogger<OutboxRepository>();
        var webLogger = _loggerFactory.CreateLogger<WebSessionizer>();
        var appLogger = _loggerFactory.CreateLogger<AppSessionizer>();
        var idleLogger = _loggerFactory.CreateLogger<IdleSessionizer>();

        var config = Options.Create(new AgentConfig());
        _outboxRepo = new OutboxRepository(config, outboxLogger);
        _outboxRepo.Init();

        var outboxService = new OutboxService(_outboxRepo);
        var identityStore = new Agent.Service.Identity.DeviceIdentityStore();

        _webSessionizer = new WebSessionizer(outboxService, webLogger);
        var appSessionizer = new AppSessionizer(outboxService, identityStore, appLogger);
        var idleSessionizer = new IdleSessionizer(outboxService, identityStore, idleLogger);

        _queue = new WebEventQueue();
        _consumerCts = new CancellationTokenSource();
        _consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in _queue.Channel.Reader.ReadAllAsync(_consumerCts.Token))
                {
                    if (_webSessionizer is not null)
                    {
                        await _webSessionizer.HandleEventAsync(evt, _deviceId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        });

        _localApi = new LocalApiHost(
            _queue,
            Token,
            appSessionizer,
            idleSessionizer,
            _webSessionizer,
            _outboxRepo,
            new OutboxSenderState(),
            _deviceId,
            "test-version");
        _localApi.Port = _port;
        await _localApi.StartAsync(CancellationToken.None);

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _client = new LocalApiClient(_httpClient, $"http://127.0.0.1:{_port}", Token);
    }

    public async Task DisposeAsync()
    {
        if (_consumerCts is not null)
        {
            _consumerCts.Cancel();
        }

        _queue?.Channel.Writer.TryComplete();

        if (_localApi is not null)
        {
            await _localApi.StopAsync(CancellationToken.None);
        }

        if (_consumer is not null)
        {
            try
            {
                await _consumer;
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        _httpClient?.Dispose();
        _loggerFactory?.Dispose();
    }

    [Fact]
    public async Task AuthSemantics_AreConsistent()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };

        var missingAuth = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.Unauthorized, missingAuth.StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/health");
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

    [Fact]
    public async Task LocalApi_AcceptsWebEvent()
    {
        var diagBefore = await _client!.GetDiagAsync(CancellationToken.None);
        Assert.True(diagBefore.Success);
        var beforeCounts = diagBefore.Value?.Outbox.PendingByType ?? new Dictionary<string, int>();

        var webEvent = new WebEvent(Guid.NewGuid(), "example.com", "Example", "https://example.com", DateTimeOffset.UtcNow, "edge");
        var res = await _client.PostWebEventAsync(webEvent, CancellationToken.None);
        Assert.True(res.Success);

        var diagAfter = await EventuallyAsync(
            () => _client.GetDiagAsync(CancellationToken.None),
            result =>
            {
                if (!result.Success || result.Value?.Outbox.PendingByType is null)
                {
                    return false;
                }

                var after = result.Value.Outbox.PendingByType;
                return GetCount(after, "web_event") >= GetCount(beforeCounts, "web_event") + 1;
            },
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(50));

        Assert.True(diagAfter.Success);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetCount(Dictionary<string, int> counts, string key)
        => counts.TryGetValue(key, out var value) ? value : 0;

    private static async Task<T> EventuallyAsync<T>(
        Func<Task<T>> action,
        Func<T, bool> predicate,
        TimeSpan timeout,
        TimeSpan interval)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var result = await action();
            if (predicate(result))
            {
                return result;
            }

            if (sw.Elapsed > timeout)
            {
                return result;
            }

            await Task.Delay(interval);
        }
    }
}
