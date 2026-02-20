namespace LuneProvisioner.Api.Infrastructure.Security;

public sealed class AccessControlOptions
{
    public const string SectionName = "AccessControl";

    public List<AccessControlToken> Tokens { get; init; } = [];
}

public sealed class AccessControlToken
{
    public string Token { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    public string[] Roles { get; init; } = [];
}
