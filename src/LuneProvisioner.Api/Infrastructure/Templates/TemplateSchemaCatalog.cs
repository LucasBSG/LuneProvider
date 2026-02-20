using System.Reflection;

namespace LuneProvisioner.Api.Infrastructure.Templates;

public static class TemplateSchemaCatalog
{
    private const string V1ResourceName = "LuneProvisioner.Api.Infrastructure.Templates.Schemas.template.schema.v1.json";

    public static string V1 { get; } = LoadEmbeddedSchema(V1ResourceName);

    private static string LoadEmbeddedSchema(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Schema resource '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Schema resource '{resourceName}' is empty.");
        }

        return content;
    }
}
