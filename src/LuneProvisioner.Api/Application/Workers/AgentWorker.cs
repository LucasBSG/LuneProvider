using System.Text.Json;
using System.Text.RegularExpressions;
using LuneProvisioner.Api.Domain;
using LuneProvisioner.Api.Domain.Entities;
using LuneProvisioner.Api.Infrastructure.Persistence;
using LuneProvisioner.Api.Infrastructure.Queues;
using LuneProvisioner.Api.Infrastructure.SignalR;
using LuneProvisioner.Api.Infrastructure.Terraform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuneProvisioner.Api.Application.Workers;

public sealed partial class AgentWorker(
    IJobQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<AgentWorkerOptions> options,
    ITerraformExecutionService terraformExecutionService,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Agent worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobId = await queue.DequeueAsync(stoppingToken);
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected worker error.");
            }
        }

        logger.LogInformation("Agent worker stopped.");
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var jobTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        jobTimeoutCts.CancelAfter(options.Value.JobExecutionTimeout);
        var executionToken = jobTimeoutCts.Token;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IJobStatusNotifier>();

        var job = await db.Jobs.FirstOrDefaultAsync(x => x.Id == jobId, executionToken);
        if (job is null)
        {
            logger.LogWarning("Job {JobId} not found.", jobId);
            return;
        }

        if (job.Status != JobStatus.Pending)
        {
            logger.LogInformation("Job {JobId} skipped because status is {Status}.", job.Id, job.Status);
            return;
        }

        var sequence = await db.AgentEvents
            .Where(x => x.JobId == job.Id)
            .Select(x => (int?)x.Sequence)
            .MaxAsync(executionToken) ?? 0;

        TerraformWorkspace? workspace = null;
        try
        {
            var template = await db.Templates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == job.TemplateId, executionToken);
            if (template is null)
            {
                throw new InvalidOperationException($"Template '{job.TemplateId}' not found for execution.");
            }

            job.Status = JobStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;
            job.CurrentStage = AgentStage.Plan;
            await db.SaveChangesAsync(executionToken);
            await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, executionToken);

            if (options.Value.UseTerraformCli)
            {
                workspace = await terraformExecutionService.PrepareWorkspaceAsync(job, template, executionToken);
                sequence = await AppendEventAsync(
                    db,
                    notifier,
                    job,
                    ++sequence,
                    AgentStage.Plan,
                    "system",
                    $"Terraform workspace prepared at {workspace.DirectoryPath}.",
                    executionToken);

                sequence = await RunTerraformStageAsync(
                    db,
                    notifier,
                    job,
                    sequence,
                    AgentStage.Plan,
                    workspace,
                    terraformExecutionService.RunPlanAsync,
                    executionToken);
            }
            else
            {
                sequence = await AppendEventAsync(
                    db,
                    notifier,
                    job,
                    ++sequence,
                    AgentStage.Plan,
                    "stdout",
                    "terraform plan - input loaded",
                    executionToken);
                await Task.Delay(TimeSpan.FromMilliseconds(400), executionToken);
            }

            job.CurrentStage = AgentStage.Validate;
            await db.SaveChangesAsync(executionToken);
            await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, executionToken);

            ValidatePreFlight(job.ParametersJson);

            if (options.Value.UseTerraformCli && workspace is not null)
            {
                sequence = await RunTerraformStageAsync(
                    db,
                    notifier,
                    job,
                    sequence,
                    AgentStage.Validate,
                    workspace,
                    terraformExecutionService.RunValidateAsync,
                    executionToken);
            }
            else
            {
                sequence = await AppendEventAsync(
                    db,
                    notifier,
                    job,
                    ++sequence,
                    AgentStage.Validate,
                    "stdout",
                    "Pre-flight completed: terraform validate",
                    executionToken);
                await Task.Delay(TimeSpan.FromMilliseconds(350), executionToken);
            }

            job.CurrentStage = AgentStage.DryRun;
            await db.SaveChangesAsync(executionToken);
            await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, executionToken);

            sequence = await ExecuteSecurityDryRunAsync(
                db,
                notifier,
                job,
                sequence,
                executionToken);

            if (job.Status == JobStatus.PendingApproval)
            {
                logger.LogInformation("Job {JobId} is waiting for delegated approval.", job.Id);
                return;
            }

            job.CurrentStage = AgentStage.Apply;
            await db.SaveChangesAsync(executionToken);
            await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, executionToken);

            if (options.Value.UseTerraformCli && workspace is not null)
            {
                sequence = await RunTerraformStageAsync(
                    db,
                    notifier,
                    job,
                    sequence,
                    AgentStage.Apply,
                    workspace,
                    terraformExecutionService.RunApplyAsync,
                    executionToken);
            }
            else
            {
                sequence = await AppendEventAsync(
                    db,
                    notifier,
                    job,
                    ++sequence,
                    AgentStage.Apply,
                    "stdout",
                    "terraform apply -auto-approve",
                    executionToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), executionToken);
            }

            job.CurrentStage = AgentStage.Output;
            job.Status = JobStatus.Succeeded;
            job.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(executionToken);
            await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, executionToken);

            await AppendEventAsync(
                db,
                notifier,
                job,
                ++sequence,
                AgentStage.Output,
                "stdout",
                "Execution completed successfully.",
                executionToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && jobTimeoutCts.IsCancellationRequested)
        {
            var timeoutException = new TimeoutException(
                $"Job execution timed out after {options.Value.JobExecutionTimeout.TotalSeconds:0.#} seconds.",
                ex);
            await MarkJobAsFailedAsync(db, notifier, job, ++sequence, timeoutException, cancellationToken);
        }
        catch (Exception ex)
        {
            await MarkJobAsFailedAsync(db, notifier, job, ++sequence, ex, cancellationToken);
        }
        finally
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync();
            }
        }
    }

    private static async Task<int> AppendEventAsync(
        AppDbContext db,
        IJobStatusNotifier notifier,
        Job job,
        int sequence,
        AgentStage stage,
        string stream,
        string message,
        CancellationToken cancellationToken)
    {
        var agentEvent = new AgentEvent
        {
            JobId = job.Id,
            Sequence = sequence,
            Stage = stage,
            Stream = stream,
            Message = message
        };

        db.AgentEvents.Add(agentEvent);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.PublishLogAsync(job.Id, agentEvent, cancellationToken);

        return sequence;
    }

    private async Task<int> RunTerraformStageAsync(
        AppDbContext db,
        IJobStatusNotifier notifier,
        Job job,
        int sequence,
        AgentStage stage,
        TerraformWorkspace workspace,
        Func<TerraformWorkspace, TerraformLogCallback, CancellationToken, Task> execute,
        CancellationToken cancellationToken)
    {
        using var sequenceGate = new SemaphoreSlim(1, 1);
        async Task HandleLogAsync(string stream, string message, CancellationToken callbackToken)
        {
            await sequenceGate.WaitAsync(callbackToken);
            try
            {
                sequence = await AppendEventAsync(
                    db,
                    notifier,
                    job,
                    ++sequence,
                    stage,
                    stream,
                    message,
                    callbackToken);
            }
            finally
            {
                sequenceGate.Release();
            }
        }

        await execute(workspace, HandleLogAsync, cancellationToken);
        return sequence;
    }

    private async Task MarkJobAsFailedAsync(
        AppDbContext db,
        IJobStatusNotifier notifier,
        Job job,
        int sequence,
        Exception ex,
        CancellationToken cancellationToken)
    {
        logger.LogError(ex, "Job {JobId} failed.", job.Id);
        job.Status = JobStatus.Failed;
        job.CurrentStage = AgentStage.Output;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.LastError = ex.Message;
        await db.SaveChangesAsync(cancellationToken);
        await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, cancellationToken);

        await AppendEventAsync(
            db,
            notifier,
            job,
            sequence,
            AgentStage.Output,
            "stderr",
            $"Execution failed: {ex.Message}",
            cancellationToken);
    }

    private async Task<int> ExecuteSecurityDryRunAsync(
        AppDbContext db,
        IJobStatusNotifier notifier,
        Job job,
        int sequence,
        CancellationToken cancellationToken)
    {
        sequence = await AppendEventAsync(
            db,
            notifier,
            job,
            ++sequence,
            AgentStage.DryRun,
            "dry-run",
            "Security dry-run started.",
            cancellationToken);

        using var doc = JsonDocument.Parse(job.ParametersJson);
        var root = doc.RootElement;
        var nodeCount = root.TryGetProperty("nodeCount", out var nodeCountNode) && nodeCountNode.TryGetInt32(out var value)
            ? value
            : 3;

        if (nodeCount <= 0)
        {
            var message = "Dry-run blocked: nodeCount must be greater than zero.";
            await AppendEventAsync(
                db,
                notifier,
                job,
                ++sequence,
                AgentStage.DryRun,
                "stderr",
                message,
                cancellationToken);
            throw new InvalidOperationException(message);
        }

        if (nodeCount > options.Value.MaxNodeCountWithoutApproval)
        {
            var message =
                $"Dry-run blocked: nodeCount={nodeCount} exceeds the policy limit of {options.Value.MaxNodeCountWithoutApproval}.";
            await AppendEventAsync(
                db,
                notifier,
                job,
                ++sequence,
                AgentStage.DryRun,
                "stderr",
                message,
                cancellationToken);
            throw new InvalidOperationException(message);
        }

        var criticalEnvironments = options.Value.CriticalEnvironments;
        if (criticalEnvironments.Any(environment =>
                string.Equals(environment, job.EnvironmentId, StringComparison.OrdinalIgnoreCase)))
        {
            if (job.ApprovalGranted)
            {
                sequence = await AppendEventAsync(
                    db,
                    notifier,
                    job,
                    ++sequence,
                    AgentStage.DryRun,
                    "dry-run",
                    $"Delegated approval already granted by {job.ApprovalGrantedBy ?? "unknown approver"}.",
                    cancellationToken);
                return sequence;
            }

            var message = "Dry-run paused: environment requires delegated approval before apply.";
            await AppendEventAsync(
                db,
                notifier,
                job,
                ++sequence,
                AgentStage.DryRun,
                "dry-run",
                message,
                cancellationToken);

            job.Status = JobStatus.PendingApproval;
            job.ApprovalRequestedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, cancellationToken);
            return sequence;
        }

        sequence = await AppendEventAsync(
            db,
            notifier,
            job,
            ++sequence,
            AgentStage.DryRun,
            "dry-run",
            "Security dry-run approved for apply.",
            cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

        return sequence;
    }

    private static void ValidatePreFlight(string parametersJson)
    {
        using var doc = JsonDocument.Parse(parametersJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("name", out var nameNode) || string.IsNullOrWhiteSpace(nameNode.GetString()))
        {
            throw new InvalidOperationException("Parameter 'name' is required.");
        }

        var name = nameNode.GetString()!;
        if (!NameRegex().IsMatch(name))
        {
            throw new InvalidOperationException("Parameter 'name' does not match the allowed pattern.");
        }

        if (!root.TryGetProperty("region", out var regionNode) || string.IsNullOrWhiteSpace(regionNode.GetString()))
        {
            throw new InvalidOperationException("Parameter 'region' is required.");
        }

        var region = regionNode.GetString()!;
        var allowedRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "us-east-1",
            "us-west-2",
            "sa-east-1"
        };

        if (!allowedRegions.Contains(region))
        {
            throw new InvalidOperationException("Parameter 'region' is outside security policy.");
        }
    }

    [GeneratedRegex("^[a-z0-9-]{3,32}$")]
    private static partial Regex NameRegex();
}
