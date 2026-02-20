using System.Diagnostics;
using System.Text.Json;
using LuneProvisioner.Api.Application.Workers;
using LuneProvisioner.Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LuneProvisioner.Api.Infrastructure.Terraform;

public sealed class TerraformExecutionService(
    IOptions<AgentWorkerOptions> options,
    ILogger<TerraformExecutionService> logger) : ITerraformExecutionService
{
    public async Task<TerraformWorkspace> PrepareWorkspaceAsync(
        Job job,
        TemplateDefinition template,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workspaceRoot = options.Value.TerraformWorkspaceRootPath;
        Directory.CreateDirectory(workspaceRoot);

        var workspacePath = Path.Combine(workspaceRoot, job.Id.ToString("N"));
        if (Directory.Exists(workspacePath))
        {
            Directory.Delete(workspacePath, recursive: true);
        }

        Directory.CreateDirectory(workspacePath);

        var mainTfPath = Path.Combine(workspacePath, "main.tf");
        await File.WriteAllTextAsync(mainTfPath, template.TerraformTemplate, cancellationToken);

        using var parametersDocument = JsonDocument.Parse(job.ParametersJson);
        var tfvarsPath = Path.Combine(workspacePath, "terraform.tfvars.json");
        await File.WriteAllTextAsync(tfvarsPath, parametersDocument.RootElement.GetRawText(), cancellationToken);

        logger.LogInformation("Prepared terraform workspace at {WorkspacePath} for job {JobId}.", workspacePath, job.Id);

        return new TerraformWorkspace(workspacePath, options.Value.CleanupTerraformWorkspace);
    }

    public Task RunPlanAsync(
        TerraformWorkspace workspace,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken)
        => RunTerraformSequenceAsync(
            workspace,
            onLog,
            cancellationToken,
            "init -input=false -no-color",
            "plan -input=false -no-color -out=tfplan");

    public Task RunValidateAsync(
        TerraformWorkspace workspace,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken)
        => RunTerraformCommandAsync(
            workspace,
            "validate -no-color",
            onLog,
            cancellationToken);

    public Task RunApplyAsync(
        TerraformWorkspace workspace,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken)
        => RunTerraformCommandAsync(
            workspace,
            "apply -input=false -no-color -auto-approve tfplan",
            onLog,
            cancellationToken);

    private async Task RunTerraformSequenceAsync(
        TerraformWorkspace workspace,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken,
        params string[] commands)
    {
        foreach (var command in commands)
        {
            await RunTerraformCommandAsync(workspace, command, onLog, cancellationToken);
        }
    }

    private async Task RunTerraformCommandAsync(
        TerraformWorkspace workspace,
        string arguments,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken)
    {
        await onLog("system", $"$ terraform {arguments}", cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.Value.TerraformBinaryPath,
            Arguments = arguments,
            WorkingDirectory = workspace.DirectoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["TF_IN_AUTOMATION"] = "1";
        startInfo.Environment["TF_INPUT"] = "0";

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

        using var killRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill failures on cancellation.
            }
        });

        var stdOutTask = PumpStreamAsync(process.StandardOutput, "stdout", onLog, cancellationToken);
        var stdErrTask = PumpStreamAsync(process.StandardError, "stderr", onLog, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdOutTask, stdErrTask);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"terraform {arguments} failed with exit code {process.ExitCode}.");
        }
    }

    private static async Task PumpStreamAsync(
        StreamReader reader,
        string stream,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await onLog(stream, line, cancellationToken);
        }
    }
}
