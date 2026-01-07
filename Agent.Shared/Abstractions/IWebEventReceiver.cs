using Agent.Shared.Models;

namespace Agent.Shared.Abstractions;

public interface IWebEventReceiver
{
    IAsyncEnumerable<WebFocusEvent> ListenAsync(CancellationToken cancellationToken);
}
