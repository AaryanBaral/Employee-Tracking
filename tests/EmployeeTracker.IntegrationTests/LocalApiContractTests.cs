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
public sealed class LocalApiContractTests : IAsyncLifetime
{
    private const string Token = "test-token";
    private WebEventQueue? _queue;
    private LocalApiHost? _localApi;
    private Task? _webEventConsumer;
    private CancellationTokenSource? _consumerCts;
    private OutboxRepository? _outboxRepo;
    private LocalApiClient? _client;
    private HttpClient? _httpClient;
    private int _port;
    private string _deviceId = "test-device";

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

        _queue = new WebEventQueue();
        _consumerCts = new CancellationTokenSource();
        _webEventConsumer = Task.Run(() => ConsumeWebEventsAsync(_queue, webSessionizer, _deviceId, _consumerCts.Token));

        _localApi = new LocalApiHost(
            _queue,
            Token,
            appSessionizer,
            idleSessionizer,
            webSessionizer,
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

        if (_localApi is not null)
        {
            await _localApi.StopAsync(CancellationToken.None);
        }

        _queue?.Channel.Writer.TryComplete();

        if (_webEventConsumer is not null)
        {
            try
            {
                await _webEventConsumer;
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        _httpClient?.Dispose();
    }

    [Fact]
    public async Task LocalApiContract_AllowsRequestsAndUpdatesOutbox()
    {
        var version = await _client!.GetVersionAsync(CancellationToken.None);
        Assert.True(version.Success);
        Assert.Equal(LocalApiConstants.Contract, version.Value?.Contract);

        var diagBefore = await _client.GetDiagAsync(CancellationToken.None);
        Assert.True(diagBefore.Success);

        var idleStart = await _client.PostIdleAsync(new IdleSampleRequest(120, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(idleStart.Success);
        var idleEnd = await _client.PostIdleAsync(new IdleSampleRequest(0, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(idleEnd.Success);

        var appFocus1 = await _client.PostAppFocusAsync(
            new AppFocusRequest("Explorer", "Start", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(appFocus1.Success);
        var appFocus2 = await _client.PostAppFocusAsync(
            new AppFocusRequest("Notepad", "Doc", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(appFocus2.Success);

        var webEvent = new WebEvent(Guid.NewGuid(), "example.com", "Example", "https://example.com", DateTimeOffset.UtcNow, "edge");
        var webResult = await _client.PostWebEventAsync(webEvent, CancellationToken.None);
        Assert.True(webResult.Success);

        var beforeCounts = diagBefore.Value?.Outbox.PendingByType ?? new Dictionary<string, int>();

        var diagAfter = await EventuallyAsync(
            () => _client.GetDiagAsync(CancellationToken.None),
            result =>
            {
                if (!result.Success || result.Value?.Outbox.PendingByType is null)
                {
                    return false;
                }

                var after = result.Value.Outbox.PendingByType;
                return GetCount(after, "idle_session") >= GetCount(beforeCounts, "idle_session") + 1
                    && GetCount(after, "app_session") >= GetCount(beforeCounts, "app_session") + 1
                    && GetCount(after, "web_event") >= GetCount(beforeCounts, "web_event") + 1;
            },
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(50));

        Assert.True(diagAfter.Success);
    }

    private static async Task ConsumeWebEventsAsync(
        WebEventQueue queue,
        WebSessionizer sessionizer,
        string deviceId,
        CancellationToken ct)
    {
        try
        {
            await foreach (var evt in queue.Channel.Reader.ReadAllAsync(ct))
            {
                await sessionizer.HandleEventAsync(evt, deviceId);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
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

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
