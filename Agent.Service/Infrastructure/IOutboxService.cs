using Agent.Service.Infrastructure.Outbox;

namespace Agent.Service.Infrastructure;

public interface IOutboxService
{
    Task EnqueueAsync(string type, object payload);
}
