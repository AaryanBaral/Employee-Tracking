using Agent.Shared.Abstractions;
using Agent.Shared.Models;

namespace Agent.Mac.Collectors;

public sealed class MacIdleCollector : IIdleCollector
{
    public async Task<IdleEvent?> GetIdleAsync(CancellationToken cancellationToken)
    {
        var output = await Shell.RunAsync("/usr/sbin/ioreg", "-c IOHIDSystem", cancellationToken);
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("HIDIdleTime", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var raw = parts[1].Trim().Trim('"');
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (!long.TryParse(digits, out var nanos))
            {
                continue;
            }

            var idle = TimeSpan.FromSeconds(nanos / 1_000_000_000.0);
            return new IdleEvent(DateTimeOffset.UtcNow, idle);
        }

        return null;
    }
}
