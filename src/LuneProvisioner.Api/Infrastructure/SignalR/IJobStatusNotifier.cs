using LuneProvisioner.Api.Domain;
using LuneProvisioner.Api.Domain.Entities;

namespace LuneProvisioner.Api.Infrastructure.SignalR;

public interface IJobStatusNotifier
{
    Task PublishStatusAsync(Guid jobId, JobStatus status, AgentStage stage, CancellationToken cancellationToken);

    Task PublishLogAsync(Guid jobId, AgentEvent agentEvent, CancellationToken cancellationToken);
}
