namespace LuneProvisioner.Api.IntegrationTests.Infrastructure;

public sealed record TemplateSummary(Guid Id, string Name, string Version, DateTime CreatedAtUtc);

public sealed record CreateJobResponse(Guid Id, string Status, string CurrentStage);

public sealed record JobEvent(int Sequence, string Stage, string Stream, string Message, DateTime TimestampUtc);

public sealed record JobDetails(
    Guid Id,
    Guid TemplateId,
    string UserId,
    string EnvironmentId,
    string Status,
    string CurrentStage,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? LastError,
    DateTime? ApprovalRequestedAtUtc,
    bool ApprovalGranted,
    string? ApprovalGrantedBy,
    DateTime? ApprovalGrantedAtUtc,
    IReadOnlyList<JobEvent> Events);
