using Agent.Shared.Abstractions;
using Agent.Shared.Models;

namespace Agent.Windows.Collectors;

public class WindowsIdleCollector : IIdleCollector
{
    public Task<IdleEvent?> GetIdleAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IdleEvent?>(null);
    }
}
