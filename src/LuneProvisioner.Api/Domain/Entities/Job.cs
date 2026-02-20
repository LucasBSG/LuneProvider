using LuneProvisioner.Api.Domain;

namespace LuneProvisioner.Api.Domain.Entities;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TemplateId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string EnvironmentId { get; set; } = string.Empty;

    public string ParametersJson { get; set; } = "{}";

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public AgentStage CurrentStage { get; set; } = AgentStage.Plan;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? LastError { get; set; }

    public DateTime? ApprovalRequestedAtUtc { get; set; }

    public bool ApprovalGranted { get; set; }

    public string? ApprovalGrantedBy { get; set; }

    public DateTime? ApprovalGrantedAtUtc { get; set; }

    public TemplateDefinition? Template { get; set; }

    public ICollection<AgentEvent> Events { get; set; } = new List<AgentEvent>();
}
