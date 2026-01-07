using Agent.Service.Infrastructure;
using Agent.Service.Infrastructure.Outbox;
using Agent.Service.Identity;
using Agent.Service.Tracking; // For internal session records
using Agent.Shared.Config;
using Agent.Shared.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace Agent.Service.Workers;

public sealed class OutboxSenderWorker : BackgroundService
{
    private readonly OutboxRepository _outbox;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentConfig _config;
    private readonly ILogger<OutboxSenderWorker> _logger;
    private readonly DeviceIdentityStore _identityStore;
    private readonly OutboxSenderState _state;
    private readonly string _instanceId = Guid.NewGuid().ToString();
    private readonly Dictionary<string, DateTimeOffset> _nextSendAt = new();

    // Mapping type -> (Endpoint, BatchSize)
    private readonly Dictionary<string, (string Endpoint, Func<AgentConfig, int> BatchSizeSelector, Func<AgentConfig, (int Min, int Max)> CadenceSelector)> _typeConfig;

    public OutboxSenderWorker(
        OutboxRepository outbox,
        IHttpClientFactory httpClientFactory,
        IOptions<AgentConfig> config,
        ILogger<OutboxSenderWorker> logger,
        DeviceIdentityStore identityStore,
        OutboxSenderState state)
    {
        _outbox = outbox;
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _logger = logger;
        _identityStore = identityStore;
        _state = state;

        _typeConfig = new()
        {
            ["web_event"] = (_config.WebEventIngestEndpoint, c => c.WebEventBatchSize, c => (c.WebEventSendMinSeconds, c.WebEventSendMaxSeconds)),
            ["web_session"] = (_config.WebSessionIngestEndpoint, c => c.WebSessionBatchSize, c => (c.WebSessionSendMinSeconds, c.WebSessionSendMaxSeconds)),
            ["app_session"] = (_config.AppSessionIngestEndpoint, c => c.AppSessionBatchSize, c => (c.AppSessionSendMinSeconds, c.AppSessionSendMaxSeconds)),
            ["idle_session"] = (_config.IdleSessionIngestEndpoint, c => c.IdleSessionBatchSize, c => (c.IdleSessionSendMinSeconds, c.IdleSessionSendMaxSeconds)),
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Max(1, _config.SenderIntervalSeconds);
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        SeedCadence();

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var type in _typeConfig.Keys)
            {
                if (stoppingToken.IsCancellationRequested) break;
                if (!IsSendDue(type))
                {
                    continue;
                }
                await ProcessTypeAsync(type, stoppingToken);
                ScheduleNextSend(type);
            }
        }
    }

    private void SeedCadence()
    {
        foreach (var type in _typeConfig.Keys)
        {
            ScheduleNextSend(type);
        }
    }

    private bool IsSendDue(string type)
    {
        if (!_nextSendAt.TryGetValue(type, out var next))
        {
            return true;
        }

        return DateTimeOffset.UtcNow >= next;
    }

    private void ScheduleNextSend(string type)
    {
        var cadence = _typeConfig[type].CadenceSelector(_config);
        var min = Math.Max(1, cadence.Min);
        var max = Math.Max(min, cadence.Max);
        var offset = Random.Shared.Next(min, max + 1);
        _nextSendAt[type] = DateTimeOffset.UtcNow.AddSeconds(offset);
    }

    private async Task ProcessTypeAsync(string type, CancellationToken ct)
    {
        try
        {
            var (endpoint, sizeSelector, _) = _typeConfig[type];
            var limit = sizeSelector(_config);
            if (limit <= 0) limit = 50;

            var batch = await _outbox.LeaseBatchAsync(type, limit, _instanceId);
            if (batch.Count == 0) return;

            var identity = await _identityStore.GetOrCreateAsync(ct);
            var deviceId = identity.DeviceId.ToString();
            var agentVersion = string.IsNullOrWhiteSpace(identity.AgentVersion) ? "unknown" : identity.AgentVersion;

            object? payloadToSend = null;
            
            // Map payloads
            if (type == "web_event")
            {
                var events = new List<WebEventBatchItem>();
                var batchId = Guid.NewGuid();
                var seq = await _outbox.GetNextSequenceAsync();
                var sentAt = DateTimeOffset.UtcNow;
                foreach (var item in batch)
                {
                    var evt = JsonSerializer.Deserialize<WebEvent>(item.PayloadJson);
                    if (evt != null)
                    {
                        events.Add(new WebEventBatchItem(
                            evt.EventId, 
                            evt.Domain,
                            evt.Title ?? "",
                            evt.Url ?? "",
                            evt.Timestamp,
                            evt.Browser
                        ));
                    }
                }
                if (events.Count > 0)
                {
                    payloadToSend = new WebEventBatchRequest(deviceId, agentVersion, batchId, seq, sentAt, events);
                }
            }
            else
            {
                // For sessions, we need BatchId and Sequence
                var batchId = Guid.NewGuid();
                var seq = await _outbox.GetNextSequenceAsync();
                var sentAt = DateTimeOffset.UtcNow;

                if (type == "web_session")
                {
                    var sessions = new List<WebSessionDto>();
                    foreach (var item in batch)
                    {
                        var rec = JsonSerializer.Deserialize<WebSessionRecord>(item.PayloadJson);
                        if (rec != null)
                        {
                            // Missing SessionId in record? 
                            // WebSessionRecord currently: Domain, Title, Url, StartAtUtc, EndAtUtc.
                            // We need to generate one or use one. Since it wasn't preserved, generate NEW one.
                            // This effectively means SessionId is transient per ingest if not stored.
                            // User warned: "Your emitted WebSessionRecord still only includes..."
                            // I should eventually add SessionId to WebSessionRecord, but for now generate new.
                            sessions.Add(new WebSessionDto(
                                rec.SessionId,
                                rec.Domain,
                                rec.Title,
                                rec.Url,
                                rec.StartAtUtc,
                                rec.EndAtUtc
                            ));
                        }
                    }
                    if (sessions.Count > 0)
                        payloadToSend = new WebSessionIngestRequest(deviceId, agentVersion, batchId, seq, sentAt, sessions);
                }
                else if (type == "app_session")
                {
                    var sessions = new List<AppSessionDto>();
                    foreach (var item in batch)
                    {
                        var rec = JsonSerializer.Deserialize<AppSessionRecord>(item.PayloadJson);
                        if (rec != null)
                        {
                            sessions.Add(new AppSessionDto(
                                rec.SessionId,
                                rec.AppName, // User said map AppName -> ProcessName
                                rec.WindowTitle,
                                rec.StartAtUtc,
                                rec.EndAtUtc
                            ));
                        }
                    }
                    if (sessions.Count > 0)
                        payloadToSend = new AppSessionIngestRequest(deviceId, agentVersion, batchId, seq, sentAt, sessions);
                }
                else if (type == "idle_session")
                {
                    var sessions = new List<IdleSessionDto>();
                    foreach (var item in batch)
                    {
                        var rec = JsonSerializer.Deserialize<IdleSessionRecord>(item.PayloadJson);
                        if (rec != null)
                        {
                            sessions.Add(new IdleSessionDto(
                                rec.SessionId,
                                rec.StartAtUtc,
                                rec.EndAtUtc
                            ));
                        }
                    }
                    if (sessions.Count > 0)
                        payloadToSend = new IdleSessionIngestRequest(deviceId, agentVersion, batchId, seq, sentAt, sessions);
                }
            }

            if (payloadToSend == null)
            {
                // All failed to parse?
                await _outbox.DeleteAsync(batch.Select(i => i.Id));
                return;
            }

            var client = _httpClientFactory.CreateClient("backend");
            var response = await client.PostAsJsonAsync(endpoint, payloadToSend, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully sent batch of {type}.", type);
                _state.MarkFlushed(DateTimeOffset.UtcNow);
                await _outbox.DeleteAsync(batch.Select(i => i.Id));
            }
            else
            {
                _logger.LogWarning("Failed to send batch of {type}. Status: {status}.", type, response.StatusCode);
                await _outbox.AbandonAsync(batch.Select(i => i.Id));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing outbox type {type}.", type);
        }
    }
}
