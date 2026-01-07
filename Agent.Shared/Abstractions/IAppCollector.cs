using Agent.Shared.Models;

namespace Agent.Shared.Abstractions;

public interface IAppCollector
{
    Task<AppFocusEvent?> GetFocusedAppAsync(CancellationToken cancellationToken);
}
