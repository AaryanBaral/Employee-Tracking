using Agent.Service.Infrastructure.Outbox;

namespace Agent.Service.Infrastructure;

public sealed class OutboxService : IOutboxService
{
    private readonly OutboxRepository _repository;

    public OutboxService(OutboxRepository repository)
    {
        _repository = repository;
    }

    public Task EnqueueAsync(string type, object payload)
    {
        return _repository.EnqueueAsync(type, payload);
    }
}
