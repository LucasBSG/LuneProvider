namespace LuneProvisioner.Api.Infrastructure.Templates;

public interface ITemplateSchemaService
{
    bool IsValid(string schemaJson, out string? error);
}
