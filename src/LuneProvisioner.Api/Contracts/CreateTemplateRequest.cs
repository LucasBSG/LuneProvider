namespace LuneProvisioner.Api.Contracts;

public sealed record CreateTemplateRequest(
    string Name,
    string Version,
    string SchemaJson,
    string TerraformTemplate);
