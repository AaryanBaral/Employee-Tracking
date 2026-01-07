using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Agent.Service.Identity;

public sealed class DeviceIdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<DeviceIdentity> GetOrCreateAsync(CancellationToken ct)
    {
        var path = GetPath();
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var existing = JsonSerializer.Deserialize<DeviceIdentity>(json, JsonOptions);
                if (existing is not null && existing.DeviceId != Guid.Empty)
                {
                    if (string.IsNullOrWhiteSpace(existing.AgentVersion))
                    {
                        existing.AgentVersion = GetAgentVersion();
                        var refreshed = JsonSerializer.Serialize(existing, JsonOptions);
                        await File.WriteAllTextAsync(path, refreshed, ct);
                    }
                    return existing;
                }
            }
            catch
            {
                // Fall through and regenerate.
            }
        }

        var created = new DeviceIdentity
        {
            DeviceId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Hostname = Environment.MachineName,
            Os = RuntimeInformation.OSDescription,
            AgentVersion = GetAgentVersion()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = JsonSerializer.Serialize(created, JsonOptions);
        await File.WriteAllTextAsync(path, payload, ct);

        return created;
    }

    private static string GetPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(baseDir, "EmployeeTracker", "device.json");
    }

    private static string GetAgentVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
    }
}

public sealed class DeviceIdentity
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? Hostname { get; set; }
    public string? Os { get; set; }
    public string? AgentVersion { get; set; }
}
