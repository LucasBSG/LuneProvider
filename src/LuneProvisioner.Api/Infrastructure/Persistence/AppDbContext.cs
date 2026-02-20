using LuneProvisioner.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuneProvisioner.Api.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<AgentEvent> AgentEvents => Set<AgentEvent>();

    public DbSet<TemplateDefinition> Templates => Set<TemplateDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TemplateDefinition>(entity =>
        {
            entity.ToTable("Templates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.Property(x => x.Version).IsRequired().HasMaxLength(30);
            entity.Property(x => x.SchemaJson).IsRequired();
            entity.Property(x => x.TerraformTemplate).IsRequired();
            entity.HasIndex(x => new { x.Name, x.Version }).IsUnique();
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("Jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).IsRequired().HasMaxLength(120);
            entity.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(80);
            entity.Property(x => x.ParametersJson).IsRequired();
            entity.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.CurrentStage).IsRequired().HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.Property(x => x.ApprovalGrantedBy).HasMaxLength(120);
            entity.HasIndex(x => new { x.EnvironmentId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });

            entity.HasOne(x => x.Template)
                .WithMany(x => x.Jobs)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AgentEvent>(entity =>
        {
            entity.ToTable("AgentEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Stream).IsRequired().HasMaxLength(16);
            entity.Property(x => x.Message).IsRequired().HasMaxLength(2000);
            entity.Property(x => x.Stage).IsRequired().HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.JobId, x.Sequence }).IsUnique();
            entity.HasIndex(x => x.TimestampUtc);

            entity.HasOne(x => x.Job)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
