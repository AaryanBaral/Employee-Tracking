using Agent.Shared.Abstractions;
using Agent.Shared.Models;

namespace Agent.Service.Collectors;

public sealed class NoopIdleCollector : IIdleCollector
{
    public Task<IdleEvent?> GetIdleAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IdleEvent?>(null);
    }
}
