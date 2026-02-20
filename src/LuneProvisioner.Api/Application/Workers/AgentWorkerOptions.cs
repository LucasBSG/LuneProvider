using System.ComponentModel.DataAnnotations;

namespace LuneProvisioner.Api.Application.Workers;

public sealed class AgentWorkerOptions
{
    public bool UseTerraformCli { get; init; } = true;

    public string TerraformBinaryPath { get; init; } = "terraform";

    public string TerraformRequiredVersion { get; init; } = "1.8.0";

    public string TerraformWorkspaceRootPath { get; init; } = Path.Combine(Path.GetTempPath(), "luneprovisioner-jobs");

    public bool CleanupTerraformWorkspace { get; init; } = true;

    [Range(1, 3600)]
    public int JobExecutionTimeoutSeconds { get; init; } = 30;

    [Range(1, 100)]
    public int MaxNodeCountWithoutApproval { get; init; } = 8;

    public string[] CriticalEnvironments { get; init; } = ["prod", "production"];

    public TimeSpan JobExecutionTimeout => TimeSpan.FromSeconds(JobExecutionTimeoutSeconds);
}
