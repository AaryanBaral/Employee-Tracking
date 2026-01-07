using Agent.Service.Infrastructure;
using Agent.Service.Infrastructure.Outbox;
using Agent.Service.Identity;
using Agent.Service.LocalApi;
using Agent.Service.Tracking;
using Agent.Shared.Abstractions;
using Agent.Shared.Config;
using Microsoft.Extensions.Options;

namespace Agent.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentConfig _config;
    private readonly IReadOnlyList<IWebEventReceiver> _browserReceivers;
    private readonly IConfiguration _configuration;
    private readonly DeviceIdentityStore _identityStore;
    private readonly WebSessionizer _sessionizer;
    private readonly AppSessionizer _appSessionizer;
    private readonly IdleSessionizer _idleSessionizer;
    private readonly OutboxRepository _outboxRepo;
    private readonly OutboxSenderState _outboxState;

    public Worker(
        ILogger<Worker> logger,
        IOptions<AgentConfig> options,
        IEnumerable<IWebEventReceiver> browserReceivers,
        IConfiguration configuration,
        DeviceIdentityStore identityStore,
        WebSessionizer sessionizer,
        AppSessionizer appSessionizer,
        IdleSessionizer idleSessionizer,
        OutboxRepository outboxRepo,
        OutboxSenderState outboxState)
    {
        _logger = logger;
        _config = options.Value;
        _browserReceivers = browserReceivers.ToList();
        _configuration = configuration;
        _identityStore = identityStore;
        _sessionizer = sessionizer;
        _appSessionizer = appSessionizer;
        _idleSessionizer = idleSessionizer;
        _outboxRepo = outboxRepo;
        _outboxState = outboxState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queue = new WebEventQueue();

        var token = _configuration["LocalApi:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            token = _config.GlobalLocalApiToken;
        }
        
        var identity = await _identityStore.GetOrCreateAsync(stoppingToken);
        var deviceId = identity.DeviceId.ToString();
        _logger.LogInformation("Device identity loaded: {deviceId}", deviceId);
        var localApiPort = _config.LocalApiPort > 0 ? _config.LocalApiPort : LocalApiHost.DefaultPort;
        LogStartupSummary(deviceId, token, localApiPort, _config.EnableLocalCollectors);

        var api = new LocalApiHost(
            queue,
            token,
            _appSessionizer,
            _idleSessionizer,
            _sessionizer,
            _outboxRepo,
            _outboxState,
            deviceId,
            identity.AgentVersion);
        api.Port = localApiPort;

        await api.StartAsync(stoppingToken);
        _logger.LogInformation("Local API started on port {port}", api.Port);

        // Web Event Loop
        try 
        {
            await foreach (var evt in queue.Channel.Reader.ReadAllAsync(stoppingToken))
            {
                // This is now async and writes to outbox
                await _sessionizer.HandleEventAsync(evt, deviceId);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing web event queue.");
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
        }
        
        // Drain Web Session
        _logger.LogInformation("Worker stopping. Draining web session.");
        try
        {
            await _sessionizer.CloseActiveAsync();
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Failed to drain web session.");
        }
    }

    private void LogStartupSummary(string deviceId, string token, int localApiPort, bool embeddedCollectorsEnabled)
    {
        var localUrl = $"http://127.0.0.1:{localApiPort}";
        var tokenState = string.IsNullOrWhiteSpace(token) ? "MISSING" : "SET";

        if (embeddedCollectorsEnabled)
        {
            _logger.LogInformation(
                """
                Agent starting
                ------------------------
                DeviceId: {deviceId}
                LocalApiUrl: {localApiUrl}
                LocalApiToken: {tokenState}
                EmbeddedCollectorPollSeconds: {pollIntervalSeconds}
                SenderIntervalSeconds: {senderIntervalSeconds}
                WebEventEndpoint: {webEventEndpoint}
                WebSessionEndpoint: {webSessionEndpoint}
                AppSessionEndpoint: {appSessionEndpoint}
                IdleSessionEndpoint: {idleSessionEndpoint}
                WebEventBatchSize: {webEventBatchSize}
                WebSessionBatchSize: {webSessionBatchSize}
                AppSessionBatchSize: {appSessionBatchSize}
                IdleSessionBatchSize: {idleSessionBatchSize}
                ------------------------
                """,
                deviceId,
                localUrl,
                tokenState,
                _config.CollectorPollSeconds,
                _config.SenderIntervalSeconds,
                _config.WebEventIngestEndpoint,
                _config.WebSessionIngestEndpoint,
                _config.AppSessionIngestEndpoint,
                _config.IdleSessionIngestEndpoint,
                _config.WebEventBatchSize,
                _config.WebSessionBatchSize,
                _config.AppSessionBatchSize,
                _config.IdleSessionBatchSize);
        }
        else
        {
            _logger.LogInformation(
                """
                Agent starting
                ------------------------
                DeviceId: {deviceId}
                LocalApiUrl: {localApiUrl}
                LocalApiToken: {tokenState}
                SenderIntervalSeconds: {senderIntervalSeconds}
                WebEventEndpoint: {webEventEndpoint}
                WebSessionEndpoint: {webSessionEndpoint}
                AppSessionEndpoint: {appSessionEndpoint}
                IdleSessionEndpoint: {idleSessionEndpoint}
                WebEventBatchSize: {webEventBatchSize}
                WebSessionBatchSize: {webSessionBatchSize}
                AppSessionBatchSize: {appSessionBatchSize}
                IdleSessionBatchSize: {idleSessionBatchSize}
                ------------------------
                """,
                deviceId,
                localUrl,
                tokenState,
                _config.SenderIntervalSeconds,
                _config.WebEventIngestEndpoint,
                _config.WebSessionIngestEndpoint,
                _config.AppSessionIngestEndpoint,
                _config.IdleSessionIngestEndpoint,
                _config.WebEventBatchSize,
                _config.WebSessionBatchSize,
                _config.AppSessionBatchSize,
                _config.IdleSessionBatchSize);
        }
    }
}
