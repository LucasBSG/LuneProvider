using LuneProvisioner.Api.Domain;

namespace LuneProvisioner.Api.Domain.Entities;

public sealed class AgentEvent
{
    public long Id { get; set; }

    public Guid JobId { get; set; }

    public int Sequence { get; set; }

    public AgentStage Stage { get; set; }

    public string Stream { get; set; } = "stdout";

    public string Message { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public Job? Job { get; set; }
}
