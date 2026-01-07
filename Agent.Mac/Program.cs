using System.Text.Json;
using Agent.Mac.Collectors;
using Agent.Shared.LocalApi;
using Agent.Shared.Models;

internal static class Program
{
    private static async Task Main()
    {
        var apiBaseUrl = Environment.GetEnvironmentVariable("AGENT_LOCAL_API_URL") ?? LocalApiConstants.DefaultBaseUrl;
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

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var apiClient = new LocalApiClient(httpClient, apiBaseUrl, token);

        var deviceId = TryLoadDeviceId() ?? "unknown";
        LogStartupSummary(deviceId, apiBaseUrl, pollSeconds);

        var health = await apiClient.GetHealthAsync(CancellationToken.None);
        if (!EnsurePreflight("health", health))
        {
            return;
        }

        var version = await apiClient.GetVersionAsync(CancellationToken.None);
        if (!EnsurePreflight("version", version))
        {
            return;
        }

        if (!string.Equals(version.Value?.Contract, LocalApiConstants.Contract, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Local API contract mismatch: {version.Value?.Contract ?? "unknown"}");
            return;
        }

        Console.WriteLine($"Local API contract: {version.Value?.Contract} DeviceId: {version.Value?.DeviceId} AgentVersion: {version.Value?.AgentVersion}");

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
                        var idleResult = await apiClient.PostIdleAsync(idlePayload, cts.Token);
                        if (!idleResult.Success)
                        {
                            LogPostFailure("idle", idleResult);
                            if (idleResult.IsUnauthorized)
                            {
                                return;
                            }
                        }
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
                        var appResult = await apiClient.PostAppFocusAsync(appPayload, cts.Token);
                        if (!appResult.Success)
                        {
                            LogPostFailure("app-focus", appResult);
                            if (appResult.IsUnauthorized)
                            {
                                return;
                            }
                        }
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

    private static bool EnsurePreflight<T>(string step, LocalApiResult<T> result)
    {
        if (result.Success)
        {
            return true;
        }

        if (result.IsUnauthorized)
        {
            Console.Error.WriteLine($"Local API {step} check unauthorized. Verify {LocalApiConstants.AuthHeaderName}.");
            return false;
        }

        Console.Error.WriteLine($"Local API {step} check failed: {result.Error ?? "unreachable"}");
        return false;
    }

    private static void LogPostFailure(string kind, LocalApiResult result)
    {
        var status = result.StatusCode is null ? "n/a" : $"{(int)result.StatusCode} {result.StatusCode}";
        var detail = string.IsNullOrWhiteSpace(result.Error) ? "unknown error" : result.Error;
        Console.Error.WriteLine($"POST /events/{kind} failed: {status} {detail}");
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
