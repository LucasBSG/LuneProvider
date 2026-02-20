namespace LuneProvisioner.Api.Infrastructure.Queues;

public interface IJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
