using Agent.Shared.Abstractions;
using Agent.Shared.Models;

namespace Agent.Mac.Collectors;

public sealed class MacAppCollector : IAppCollector
{
    public async Task<AppFocusEvent?> GetFocusedAppAsync(CancellationToken cancellationToken)
    {
        var script = """
            tell application "System Events"
                set frontApp to first application process whose frontmost is true
                set appName to name of frontApp
                set winTitle to ""
                try
                    set winTitle to name of front window of frontApp
                end try
                return appName & "||" & winTitle
            end tell
            """;
        var result = await Shell.RunAsync(
            "/usr/bin/osascript",
            new[] { "-e", script },
            cancellationToken);
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        var parts = result.Split("||", 2, StringSplitOptions.None);
        var appName = parts[0].Trim();
        var windowTitle = parts.Length > 1 ? parts[1].Trim() : null;
        if (string.IsNullOrWhiteSpace(appName))
        {
            return null;
        }

        return new AppFocusEvent(DateTimeOffset.UtcNow, appName, string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle);
    }
}
