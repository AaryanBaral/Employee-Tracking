using System.Net.Http.Json;
using System.Text.Json;
using Agent.Mac.Collectors;

internal static class Program
{
    private static async Task Main()
    {
        var apiBaseUrl = Environment.GetEnvironmentVariable("AGENT_LOCAL_API_URL") ?? "http://127.0.0.1:43121";
        var token = Environment.GetEnvironmentVariable("AGENT_LOCAL_API_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("AGENT_LOCAL_API_TOKEN is required.");
            return;
        }

        var pollSecondsValue = Environment.GetEnvironmentVariable("AGENT_POLL_SECONDS");
        var pollSeconds = int.TryParse(pollSecondsValue, out var parsed) && parsed > 0 ? parsed : 1;

        var appCollector = new MacAppCollector();
        var idleCollector = new MacIdleCollector();

        using var client = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        client.DefaultRequestHeaders.Add("X-Agent-Token", token);

        var deviceId = TryLoadDeviceId() ?? "unknown";
        LogStartupSummary(deviceId, apiBaseUrl, pollSeconds);

        if (!await IsLocalApiHealthyAsync(client))
        {
            Console.Error.WriteLine("Local API health check failed. Ensure Agent.Service is running and token is valid.");
            return;
        }

        var version = await GetLocalApiVersionAsync(client);
        if (version is null)
        {
            Console.Error.WriteLine("Local API version check failed.");
            return;
        }

        if (!string.Equals(version.Contract, "local-api-v1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Local API contract mismatch: {version.Contract}");
            return;
        }

        Console.WriteLine($"Local API contract: {version.Contract} DeviceId: {version.DeviceId} AgentVersion: {version.AgentVersion}");

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
        Console.WriteLine("Agent.Mac started.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                try
                {
                    var idle = await idleCollector.GetIdleAsync(cts.Token);
                    if (idle is not null)
                    {
                        var idlePayload = new IdleSampleRequest(idle.IdleDuration.TotalSeconds, idle.TimestampUtc);
                        await client.PostAsJsonAsync("/events/idle", idlePayload, cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Idle sample failed: {ex.Message}");
                }

                try
                {
                    var app = await appCollector.GetFocusedAppAsync(cts.Token);
                    if (app is not null && !string.IsNullOrWhiteSpace(app.AppName))
                    {
                        var appPayload = new AppFocusRequest(app.AppName, app.WindowTitle, app.TimestampUtc);
                        await client.PostAsJsonAsync("/events/app-focus", appPayload, cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"App focus sample failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        Console.WriteLine("Agent.Mac stopping.");
    }

    private sealed record AppFocusRequest(string AppName, string? WindowTitle, DateTimeOffset TimestampUtc);
    private sealed record IdleSampleRequest(double IdleSeconds, DateTimeOffset TimestampUtc);
    private sealed record LocalHealthResponse(bool Ok);
    private sealed record LocalVersionResponse(
        string Contract,
        string? DeviceId,
        string? AgentVersion,
        int Port);

    private static async Task<bool> IsLocalApiHealthyAsync(HttpClient client)
    {
        try
        {
            using var response = await client.GetAsync("/health");
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Local API health status failed: {(int)response.StatusCode} {response.StatusCode}");
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<LocalHealthResponse>();
            return payload?.Ok ?? true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Local API health check error: {ex.Message}");
            return false;
        }
    }

    private static async Task<LocalVersionResponse?> GetLocalApiVersionAsync(HttpClient client)
    {
        try
        {
            using var response = await client.GetAsync("/version");
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Local API version status failed: {(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LocalVersionResponse>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Local API version check error: {ex.Message}");
            return null;
        }
    }

    private static void LogStartupSummary(string deviceId, string localApiUrl, int pollSeconds)
    {
        Console.WriteLine(
            """
            Agent starting
            ------------------------
            DeviceId: {0}
            LocalApiUrl: {1}
            LocalApiToken: SET
            PollIntervalSeconds: {2}
            ------------------------
            """,
            deviceId,
            localApiUrl,
            pollSeconds);
    }

    private static string? TryLoadDeviceId()
    {
        try
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = Path.Combine(baseDir, "EmployeeTracker", "device.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("DeviceId", out var deviceId))
            {
                return deviceId.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
