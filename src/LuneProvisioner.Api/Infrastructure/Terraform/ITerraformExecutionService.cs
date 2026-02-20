using LuneProvisioner.Api.Domain.Entities;

namespace LuneProvisioner.Api.Infrastructure.Terraform;

public delegate Task TerraformLogCallback(string stream, string message, CancellationToken cancellationToken);

public interface ITerraformExecutionService
{
    Task<TerraformWorkspace> PrepareWorkspaceAsync(
        Job job,
        TemplateDefinition template,
        CancellationToken cancellationToken);

    Task RunPlanAsync(
        TerraformWorkspace workspace,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken);

    Task RunValidateAsync(
        TerraformWorkspace workspace,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken);

    Task RunApplyAsync(
        TerraformWorkspace workspace,
        TerraformLogCallback onLog,
        CancellationToken cancellationToken);
}
