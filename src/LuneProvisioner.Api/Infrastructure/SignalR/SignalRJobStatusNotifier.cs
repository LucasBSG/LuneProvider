using LuneProvisioner.Api.Domain;
using LuneProvisioner.Api.Domain.Entities;
using Microsoft.AspNetCore.SignalR;

namespace LuneProvisioner.Api.Infrastructure.SignalR;

public sealed class SignalRJobStatusNotifier(IHubContext<JobStatusHub> hubContext) : IJobStatusNotifier
{
    public Task PublishStatusAsync(Guid jobId, JobStatus status, AgentStage stage, CancellationToken cancellationToken)
        => hubContext.Clients.Group(JobStatusHub.GroupName(jobId)).SendAsync(
            "job-status",
            new
            {
                jobId,
                status = status.ToString(),
                stage = stage.ToString(),
                timestampUtc = DateTime.UtcNow
            },
            cancellationToken);

    public Task PublishLogAsync(Guid jobId, AgentEvent agentEvent, CancellationToken cancellationToken)
        => hubContext.Clients.Group(JobStatusHub.GroupName(jobId)).SendAsync(
            "job-log",
            new
            {
                jobId,
                sequence = agentEvent.Sequence,
                stage = agentEvent.Stage.ToString(),
                stream = agentEvent.Stream,
                message = agentEvent.Message,
                timestampUtc = agentEvent.TimestampUtc
            },
            cancellationToken);
}
