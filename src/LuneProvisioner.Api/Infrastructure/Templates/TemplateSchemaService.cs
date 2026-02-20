using System.Text.Json;

namespace LuneProvisioner.Api.Infrastructure.Templates;

public sealed class TemplateSchemaService : ITemplateSchemaService
{
    public bool IsValid(string schemaJson, out string? error)
    {
        error = null;

        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Schema root must be an object.";
                return false;
            }

            if (!root.TryGetProperty("type", out var typeNode) || typeNode.GetString() != "object")
            {
                error = "Schema type must be object.";
                return false;
            }

            if (!root.TryGetProperty("properties", out var propertiesNode) || propertiesNode.ValueKind != JsonValueKind.Object)
            {
                error = "Schema must contain properties object.";
                return false;
            }

            if (!root.TryGetProperty("required", out var requiredNode) || requiredNode.ValueKind != JsonValueKind.Array)
            {
                error = "Schema must contain required array.";
                return false;
            }

            if (requiredNode.GetArrayLength() == 0)
            {
                error = "Schema required array cannot be empty.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Schema JSON is invalid.";
            return false;
        }
    }
}
