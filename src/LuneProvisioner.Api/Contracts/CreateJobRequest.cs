using System.Text.Json;

namespace LuneProvisioner.Api.Contracts;

public sealed record CreateJobRequest(
    Guid TemplateId,
    string UserId,
    string EnvironmentId,
    JsonElement Parameters);
