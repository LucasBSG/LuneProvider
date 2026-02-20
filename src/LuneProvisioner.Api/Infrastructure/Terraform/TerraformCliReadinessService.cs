using System.Diagnostics;
using System.Text.Json;
using LuneProvisioner.Api.Application.Workers;
using Microsoft.Extensions.Options;

namespace LuneProvisioner.Api.Infrastructure.Terraform;

public sealed class TerraformCliReadinessService(
    IOptions<AgentWorkerOptions> options,
    ILogger<TerraformCliReadinessService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.UseTerraformCli)
        {
            logger.LogInformation("Terraform CLI readiness skipped because AgentWorker:UseTerraformCli=false.");
            return;
        }

        var requiredVersionText = options.Value.TerraformRequiredVersion;
        if (!TerraformVersionParser.TryParse(requiredVersionText, out var requiredVersion))
        {
            throw new InvalidOperationException(
                $"AgentWorker:TerraformRequiredVersion '{requiredVersionText}' is invalid.");
        }

        var detectedVersionText = await ReadTerraformVersionAsync(cancellationToken);
        if (!TerraformVersionParser.TryParse(detectedVersionText, out var detectedVersion))
        {
            throw new InvalidOperationException(
                $"Unable to parse Terraform version '{detectedVersionText}' from binary '{options.Value.TerraformBinaryPath}'.");
        }

        if (detectedVersion < requiredVersion)
        {
            throw new InvalidOperationException(
                $"Terraform version {detectedVersionText} is lower than required minimum {requiredVersionText}.");
        }

        logger.LogInformation(
            "Terraform CLI readiness check passed. Binary: {TerraformBinaryPath}; version: {DetectedVersion}; minimum required: {RequiredVersion}.",
            options.Value.TerraformBinaryPath,
            detectedVersionText,
            requiredVersionText);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<string> ReadTerraformVersionAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.Value.TerraformBinaryPath,
            Arguments = "version -json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Terraform process failed to start.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start terraform binary '{options.Value.TerraformBinaryPath}'.",
                ex);
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var errorText = string.IsNullOrWhiteSpace(stderr) ? "No error output." : stderr.Trim();
            throw new InvalidOperationException(
                $"terraform version -json failed with exit code {process.ExitCode}. {errorText}");
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.TryGetProperty("terraform_version", out var versionProperty))
            {
                var version = versionProperty.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version!;
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("terraform version -json returned invalid JSON.", ex);
        }

        throw new InvalidOperationException("terraform version -json output did not contain terraform_version.");
    }
}
