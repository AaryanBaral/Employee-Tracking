using Agent.Shared.Abstractions;
using Agent.Shared.Models;

namespace Agent.Windows.Collectors;

public class WindowsAppCollector : IAppCollector
{
    public Task<AppFocusEvent?> GetFocusedAppAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<AppFocusEvent?>(null);
    }
}
