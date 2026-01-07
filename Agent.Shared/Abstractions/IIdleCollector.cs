using Agent.Shared.Models;

namespace Agent.Shared.Abstractions;

public interface IIdleCollector
{
    Task<IdleEvent?> GetIdleAsync(CancellationToken cancellationToken);
}
