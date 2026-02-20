namespace LuneProvisioner.Api.Infrastructure.Terraform;

public sealed class TerraformWorkspace(string directoryPath, bool cleanupOnDispose) : IAsyncDisposable
{
    public string DirectoryPath { get; } = directoryPath;

    public ValueTask DisposeAsync()
    {
        if (!cleanupOnDispose)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures to avoid masking worker result.
        }

        return ValueTask.CompletedTask;
    }
}
