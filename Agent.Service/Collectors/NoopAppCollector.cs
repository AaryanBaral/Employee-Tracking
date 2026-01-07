using Agent.Shared.Abstractions;
using Agent.Shared.Models;

namespace Agent.Service.Collectors;

public sealed class NoopAppCollector : IAppCollector
{
    public Task<AppFocusEvent?> GetFocusedAppAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<AppFocusEvent?>(null);
    }
}
