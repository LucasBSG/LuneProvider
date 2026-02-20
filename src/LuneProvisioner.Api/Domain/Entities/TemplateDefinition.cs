namespace LuneProvisioner.Api.Domain.Entities;

public sealed class TemplateDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0";

    public string SchemaJson { get; set; } = string.Empty;

    public string TerraformTemplate { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
