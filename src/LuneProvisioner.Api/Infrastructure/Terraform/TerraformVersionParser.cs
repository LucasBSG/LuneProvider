using System.Text.RegularExpressions;

namespace LuneProvisioner.Api.Infrastructure.Terraform;

public static partial class TerraformVersionParser
{
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var match = TerraformVersionRegex().Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        var parts = match.Value.Split('.');
        var normalizedParts = new string[3];
        normalizedParts[0] = parts.ElementAtOrDefault(0) ?? "0";
        normalizedParts[1] = parts.ElementAtOrDefault(1) ?? "0";
        normalizedParts[2] = parts.ElementAtOrDefault(2) ?? "0";

        if (!Version.TryParse(string.Join('.', normalizedParts), out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    [GeneratedRegex(@"^\d+(?:\.\d+){0,2}")]
    private static partial Regex TerraformVersionRegex();
}
