using Agent.Shared.LocalApi;
using Agent.Shared.Models;
using Agent.Windows.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static class Program
{
    private const int ExitMissingToken = 2;
    private const int ExitUnauthorized = 3;
    private const int ExitUnreachable = 4;
    private const int ExitContractMismatch = 5;
    private const int ExitRepeatedFailures = 6;

    private const string LocalApiUrlEnv = "AGENT_LOCAL_API_URL";
    private const string LocalApiTokenEnv = "AGENT_LOCAL_API_TOKEN";
    private const string PollSecondsEnv = "AGENT_POLL_SECONDS";
    private const string FailureExitSecondsEnv = "AGENT_FAILURE_EXIT_SECONDS";

    private static async Task Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices(services =>
            {
                services.AddHostedService<AgentWindowsWorker>();
            })
            .Build();

        await host.RunAsync();
    }

    private sealed class AgentWindowsWorker : BackgroundService
    {
        private readonly IHostApplicationLifetime _lifetime;

        public AgentWindowsWorker(IHostApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var exitCode = await RunAsync(stoppingToken);
            if (exitCode != 0)
            {
                Environment.ExitCode = exitCode;
                _lifetime.StopApplication();
            }
        }
    }

    private static async Task<int> RunAsync(CancellationToken stoppingToken)
    {
        var apiBaseUrl = Environment.GetEnvironmentVariable(LocalApiUrlEnv) ?? LocalApiConstants.DefaultBaseUrl;
        var token = Environment.GetEnvironmentVariable(LocalApiTokenEnv);
        var pollSeconds = ReadIntEnv(PollSecondsEnv, 1);
        var failureExitSeconds = ReadIntEnv(FailureExitSecondsEnv, 60);

        LogStartupSummary(apiBaseUrl, token, pollSeconds);

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("AGENT_LOCAL_API_TOKEN is required.");
            return ExitMissingToken;
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var apiClient = new LocalApiClient(httpClient, apiBaseUrl, token);

        var health = await apiClient.GetHealthAsync(stoppingToken);
        if (!health.Success)
        {
            return HandlePreflightFailure("health", health);
        }

        var version = await apiClient.GetVersionAsync(stoppingToken);
        if (!version.Success)
        {
            return HandlePreflightFailure("version", version);
        }

        if (!string.Equals(version.Value?.Contract, LocalApiConstants.Contract, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Local API contract mismatch: {version.Value?.Contract ?? "unknown"}");
            return ExitContractMismatch;
        }

        LogConnected(version.Value);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
        var lastSuccessAt = DateTimeOffset.UtcNow;
        var failureWindow = TimeSpan.FromSeconds(Math.Max(5, failureExitSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;
                var hadSuccess = false;

                var idleSeconds = WindowsInput.GetIdleSeconds();
                var idleRequest = new IdleSampleRequest(idleSeconds, now);
                var idleResult = await apiClient.PostIdleAsync(idleRequest, stoppingToken);
                if (idleResult.Success)
                {
                    hadSuccess = true;
                }
                else
                {
                    LogPostFailure("idle", idleResult);
                    if (idleResult.IsUnauthorized)
                    {
                        return ExitUnauthorized;
                    }
                }

                var foreground = WindowsInput.GetForegroundApp();
                if (foreground is not null && !string.IsNullOrWhiteSpace(foreground.AppName))
                {
                    var appName = TruncateRequired(foreground.AppName, 128);
                    var windowTitle = TruncateOptional(foreground.WindowTitle, 256);
                    var appRequest = new AppFocusRequest(appName, windowTitle, now);
                    var appResult = await apiClient.PostAppFocusAsync(appRequest, stoppingToken);
                    if (appResult.Success)
                    {
                        hadSuccess = true;
                    }
                    else
                    {
                        LogPostFailure("app-focus", appResult);
                        if (appResult.IsUnauthorized)
                        {
                            return ExitUnauthorized;
                        }
                    }
                }

                if (hadSuccess)
                {
                    lastSuccessAt = DateTimeOffset.UtcNow;
                    continue;
                }

                if (DateTimeOffset.UtcNow - lastSuccessAt > failureWindow)
                {
                    Console.Error.WriteLine($"Local API POST failures exceeded {failureWindow.TotalSeconds:0} seconds. Exiting.");
                    return ExitRepeatedFailures;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        Console.WriteLine("Agent.Windows stopping.");
        return 0;
    }

    private static int HandlePreflightFailure(string step, LocalApiResult result)
    {
        if (result.IsUnauthorized)
        {
            Console.Error.WriteLine($"Local API {step} check unauthorized. Verify {LocalApiConstants.AuthHeaderName}.");
            return ExitUnauthorized;
        }

        Console.Error.WriteLine($"Local API {step} check failed: {result.Error ?? "unreachable"}");
        return ExitUnreachable;
    }

    private static int HandlePreflightFailure(string step, LocalApiResult<LocalHealthResponse> result)
    {
        if (result.IsUnauthorized)
        {
            Console.Error.WriteLine($"Local API {step} check unauthorized. Verify {LocalApiConstants.AuthHeaderName}.");
            return ExitUnauthorized;
        }

        Console.Error.WriteLine($"Local API {step} check failed: {result.Error ?? "unreachable"}");
        return ExitUnreachable;
    }

    private static int HandlePreflightFailure(string step, LocalApiResult<LocalVersionResponse> result)
    {
        if (result.IsUnauthorized)
        {
            Console.Error.WriteLine($"Local API {step} check unauthorized. Verify {LocalApiConstants.AuthHeaderName}.");
            return ExitUnauthorized;
        }

        Console.Error.WriteLine($"Local API {step} check failed: {result.Error ?? "unreachable"}");
        return ExitUnreachable;
    }

    private static void LogStartupSummary(string localApiUrl, string? token, int pollSeconds)
    {
        var tokenState = string.IsNullOrWhiteSpace(token) ? "MISSING" : "SET";
        Console.WriteLine(
            """
            Agent starting
            ------------------------
            OS: Windows
            LocalApiUrl: {0}
            LocalApiToken: {1}
            PollIntervalSeconds: {2}
            ------------------------
            """,
            localApiUrl,
            tokenState,
            pollSeconds);
    }

    private static void LogConnected(LocalVersionResponse? version)
    {
        Console.WriteLine(
            """
            Connected to Local API
            ------------------------
            contract: {0}
            deviceId: {1}
            agentVersion: {2}
            ------------------------
            """,
            version?.Contract ?? "unknown",
            version?.DeviceId ?? "unknown",
            version?.AgentVersion ?? "unknown");
    }

    private static void LogPostFailure(string kind, LocalApiResult result)
    {
        var status = result.StatusCode is null ? "n/a" : $"{(int)result.StatusCode} {result.StatusCode}";
        var detail = string.IsNullOrWhiteSpace(result.Error) ? "unknown error" : result.Error;
        Console.Error.WriteLine($"POST /events/{kind} failed: {status} {detail}");
    }

    private static string TruncateRequired(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TruncateOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static int ReadIntEnv(string name, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : defaultValue;
    }
}