using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LuneProvisioner.Api.IntegrationTests.Infrastructure;

public sealed class LuneApiWebApplicationFactory(int jobExecutionTimeoutSeconds = 30) : WebApplicationFactory<Program>
{
    private readonly string _databaseFilePath = Path.Combine(
        Path.GetTempPath(),
        $"luneprovisioner-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_databaseFilePath}",
                ["AgentWorker:UseTerraformCli"] = "false",
                ["AgentWorker:JobExecutionTimeoutSeconds"] = jobExecutionTimeoutSeconds.ToString(),
                ["AccessControl:Tokens:0:Token"] = "test-approver-token",
                ["AccessControl:Tokens:0:UserId"] = "security-lead",
                ["AccessControl:Tokens:0:Roles:0"] = "Approver"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        DeleteIfExists(_databaseFilePath);
        DeleteIfExists($"{_databaseFilePath}-shm");
        DeleteIfExists($"{_databaseFilePath}-wal");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
