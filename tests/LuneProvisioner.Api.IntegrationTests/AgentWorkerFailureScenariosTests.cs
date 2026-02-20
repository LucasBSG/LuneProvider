using System.Net;
using FluentAssertions;
using LuneProvisioner.Api.IntegrationTests.Infrastructure;

namespace LuneProvisioner.Api.IntegrationTests;

public sealed class AgentWorkerFailureScenariosTests
{
    [Fact]
    public async Task Should_mark_job_as_failed_when_preflight_validation_fails()
    {
        using var factory = new LuneApiWebApplicationFactory();
        using var client = factory.CreateClient();
        var templateId = await client.GetSeededTemplateIdAsync();

        var jobId = await client.CreateJobAsync(
            templateId,
            "integration-user",
            "qa",
            new
            {
                name = "lune-qa",
                region = "eu-west-1",
                nodeCount = 3
            });

        var details = await client.WaitForCompletionAsync(jobId, TimeSpan.FromSeconds(8));

        details.Status.Should().Be("Failed");
        details.CurrentStage.Should().Be("Output");
        details.LastError.Should().Contain("outside security policy");
        details.Events.Should().Contain(x => x.Stream == "stderr");
    }

    [Fact]
    public async Task Should_mark_job_as_failed_when_execution_times_out()
    {
        using var factory = new LuneApiWebApplicationFactory(jobExecutionTimeoutSeconds: 1);
        using var client = factory.CreateClient();
        var templateId = await client.GetSeededTemplateIdAsync();

        var jobId = await client.CreateJobAsync(
            templateId,
            "integration-user",
            "qa",
            new
            {
                name = "lune-timeout",
                region = "sa-east-1",
                nodeCount = 3
            });

        var details = await client.WaitForCompletionAsync(jobId, TimeSpan.FromSeconds(8));

        details.Status.Should().Be("Failed");
        details.CurrentStage.Should().Be("Output");
        details.LastError.Should().Contain("timed out");
    }

    [Fact]
    public async Task Should_block_apply_when_dry_run_policy_rejects_node_count()
    {
        using var factory = new LuneApiWebApplicationFactory();
        using var client = factory.CreateClient();
        var templateId = await client.GetSeededTemplateIdAsync();

        var jobId = await client.CreateJobAsync(
            templateId,
            "integration-user",
            "qa",
            new
            {
                name = "lune-overprovisioned",
                region = "sa-east-1",
                nodeCount = 12
            });

        var details = await client.WaitForCompletionAsync(jobId, TimeSpan.FromSeconds(8));

        details.Status.Should().Be("Failed");
        details.LastError.Should().Contain("Dry-run blocked");
        details.Events.Should().Contain(x => x.Stage == "DryRun" && x.Stream == "stderr");
    }

    [Fact]
    public async Task Should_pause_for_approval_and_resume_execution_for_critical_environment()
    {
        using var factory = new LuneApiWebApplicationFactory();
        using var client = factory.CreateClient();
        var templateId = await client.GetSeededTemplateIdAsync();

        var jobId = await client.CreateJobAsync(
            templateId,
            "integration-user",
            "production",
            new
            {
                name = "lune-prod",
                region = "sa-east-1",
                nodeCount = 3
            });

        var pendingApproval = await client.WaitForStatusAsync(jobId, TimeSpan.FromSeconds(8), "PendingApproval");
        pendingApproval.CurrentStage.Should().Be("DryRun");
        pendingApproval.ApprovalRequestedAtUtc.Should().NotBeNull();

        await client.ApproveJobAsync(jobId, "test-approver-token");
        var completed = await client.WaitForStatusAsync(jobId, TimeSpan.FromSeconds(8), "Succeeded");

        completed.Status.Should().Be("Succeeded");
        completed.ApprovalGranted.Should().BeTrue();
        completed.ApprovalGrantedBy.Should().Be("security-lead");
        completed.Events.Should().Contain(x => x.Message.Contains("Delegated approval granted"));
    }

    [Fact]
    public async Task Should_require_authentication_for_job_approval_endpoint()
    {
        using var factory = new LuneApiWebApplicationFactory();
        using var client = factory.CreateClient();
        var templateId = await client.GetSeededTemplateIdAsync();

        var jobId = await client.CreateJobAsync(
            templateId,
            "integration-user",
            "production",
            new
            {
                name = "lune-prod-auth",
                region = "sa-east-1",
                nodeCount = 3
            });

        await client.WaitForStatusAsync(jobId, TimeSpan.FromSeconds(8), "PendingApproval");

        var unauthenticated = await client.PostAsync($"/jobs/{jobId}/approve", null);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
