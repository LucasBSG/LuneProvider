using System.Text.Json;

namespace LuneProvisioner.Api.Contracts;

public sealed record JobDetailsResponse(
    Guid Id,
    Guid TemplateId,
    string UserId,
    string EnvironmentId,
    string Status,
    string CurrentStage,
    JsonElement Parameters,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? LastError,
    DateTime? ApprovalRequestedAtUtc,
    bool ApprovalGranted,
    string? ApprovalGrantedBy,
    DateTime? ApprovalGrantedAtUtc,
    IReadOnlyList<JobEventResponse> Events);

public sealed record JobEventResponse(
    int Sequence,
    string Stage,
    string Stream,
    string Message,
    DateTime TimestampUtc);
