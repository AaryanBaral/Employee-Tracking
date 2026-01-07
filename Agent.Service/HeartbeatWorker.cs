using System.Net.Http.Json;
using Agent.Service.Identity;
using Agent.Shared.Config;
using Microsoft.Extensions.Options;

namespace Agent.Service;

public sealed class HeartbeatWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeviceIdentityStore _identityStore;
    private readonly AgentConfig _config;
    private readonly ILogger<HeartbeatWorker> _logger;

    public HeartbeatWorker(
        IHttpClientFactory httpClientFactory,
        DeviceIdentityStore identityStore,
        IOptions<AgentConfig> config,
        ILogger<HeartbeatWorker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _identityStore = identityStore;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = await _identityStore.GetOrCreateAsync(stoppingToken);
        var deviceId = identity.DeviceId.ToString();
        var hostname = identity.Hostname;

        var intervalSeconds = _config.HeartbeatIntervalSeconds <= 0 ? 60 : _config.HeartbeatIntervalSeconds;
        var backoffSeconds = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var ok = await SendHeartbeatAsync(deviceId, hostname, stoppingToken);
            if (ok)
            {
                backoffSeconds = 0;
            }
            else
            {
                backoffSeconds = backoffSeconds == 0 ? 5 : Math.Min(backoffSeconds * 2, 60);
            }

            var delay = TimeSpan.FromSeconds(intervalSeconds + backoffSeconds);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> SendHeartbeatAsync(string deviceId, string? hostname, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("backend");
            var req = new DeviceHeartbeatRequest(
                deviceId,
                hostname,
                DateTimeOffset.UtcNow);

            var resp = await client.PostAsJsonAsync(_config.DeviceHeartbeatEndpoint, req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Heartbeat failed: {status}", resp.StatusCode);
                return false;
            }

            _logger.LogDebug("Heartbeat OK for device {deviceId}", deviceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat error for device {deviceId}", deviceId);
            return false;
        }
    }

    private sealed record DeviceHeartbeatRequest(
        string DeviceId,
        string? Hostname,
        DateTimeOffset LastSeenAt);
}
