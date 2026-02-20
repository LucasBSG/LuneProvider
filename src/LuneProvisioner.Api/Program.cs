using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data.Common;
using System.Security.Claims;
using LuneProvisioner.Api.Application.Workers;
using LuneProvisioner.Api.Contracts;
using LuneProvisioner.Api.Domain;
using LuneProvisioner.Api.Domain.Entities;
using LuneProvisioner.Api.Infrastructure.Persistence;
using LuneProvisioner.Api.Infrastructure.Queues;
using LuneProvisioner.Api.Infrastructure.Security;
using LuneProvisioner.Api.Infrastructure.SignalR;
using LuneProvisioner.Api.Infrastructure.Terraform;
using LuneProvisioner.Api.Infrastructure.Templates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const string FrontendCorsPolicy = "FrontendCorsPolicy";

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173"];

        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});
builder.Services.AddSignalR();
builder.Services
    .AddOptions<AgentWorkerOptions>()
    .BindConfiguration("AgentWorker")
    .ValidateDataAnnotations()
    .Validate(
        options => !options.UseTerraformCli || TerraformVersionParser.TryParse(options.TerraformRequiredVersion, out _),
        "AgentWorker:TerraformRequiredVersion must be a valid semantic version.")
    .ValidateOnStart();
builder.Services
    .AddOptions<AccessControlOptions>()
    .BindConfiguration(AccessControlOptions.SectionName)
    .Validate(options => options.Tokens.Count > 0, "At least one access token must be configured.")
    .ValidateOnStart();
builder.Services
    .AddAuthentication("LuneToken")
    .AddScheme<AuthenticationSchemeOptions, LuneTokenAuthenticationHandler>("LuneToken", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanApproveInfrastructure", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Approver", "Admin");
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<IJobQueue, InMemoryJobQueue>();
builder.Services.AddSingleton<IJobStatusNotifier, SignalRJobStatusNotifier>();
builder.Services.AddSingleton<ITerraformExecutionService, TerraformExecutionService>();
builder.Services.AddScoped<ITemplateSchemaService, TemplateSchemaService>();
builder.Services.AddHostedService<TerraformCliReadinessService>();
builder.Services.AddHostedService<AgentWorker>();

var app = builder.Build();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    EnsureSqliteCompatibility(db);

    if (!db.Templates.Any())
    {
        db.Templates.Add(new TemplateDefinition
        {
            Name = "k8s-cluster-base",
            Version = "1.0.0",
            SchemaJson = TemplateSchemaCatalog.V1,
            TerraformTemplate = """
                terraform {
                  required_version = ">= 1.8.0"
                }

                variable "name" {
                  type = string
                }

                variable "region" {
                  type = string
                }
                """
        });
        db.SaveChanges();
    }
}

app.MapGet("/", () => Results.Ok(new
{
    service = "LuneProvisioner.Api",
    status = "running"
}));

app.MapGet("/schemas/template/v1", () => Results.Text(TemplateSchemaCatalog.V1, "application/json"));

app.MapPost("/templates", async (
    CreateTemplateRequest request,
    AppDbContext db,
    ITemplateSchemaService schemaService,
    CancellationToken cancellationToken) =>
{
    if (!schemaService.IsValid(request.SchemaJson, out var error))
    {
        return Results.BadRequest(new { error });
    }

    var template = new TemplateDefinition
    {
        Name = request.Name.Trim(),
        Version = request.Version.Trim(),
        SchemaJson = request.SchemaJson,
        TerraformTemplate = request.TerraformTemplate
    };

    db.Templates.Add(template);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/templates/{template.Id}", new
    {
        template.Id,
        template.Name,
        template.Version
    });
});

app.MapGet("/templates", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var templates = await db.Templates
        .AsNoTracking()
        .OrderBy(x => x.Name)
        .Select(x => new
        {
            x.Id,
            x.Name,
            x.Version,
            x.CreatedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(templates);
});

app.MapPost("/jobs", async (
    CreateJobRequest request,
    AppDbContext db,
    IJobQueue queue,
    IJobStatusNotifier notifier,
    CancellationToken cancellationToken) =>
{
    var template = await db.Templates
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == request.TemplateId, cancellationToken);

    if (template is null)
    {
        return Results.NotFound(new { error = "Template not found." });
    }

    var job = new Job
    {
        TemplateId = request.TemplateId,
        UserId = request.UserId.Trim(),
        EnvironmentId = request.EnvironmentId.Trim(),
        ParametersJson = request.Parameters.GetRawText()
    };

    db.Jobs.Add(job);
    db.AgentEvents.Add(new AgentEvent
    {
        JobId = job.Id,
        Sequence = 1,
        Stage = AgentStage.Plan,
        Stream = "system",
        Message = "Job queued."
    });
    await db.SaveChangesAsync(cancellationToken);

    await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, cancellationToken);
    await queue.EnqueueAsync(job.Id, cancellationToken);

    return Results.Accepted($"/jobs/{job.Id}", new
    {
        job.Id,
        status = job.Status.ToString(),
        currentStage = job.CurrentStage.ToString()
    });
});

app.MapPost("/jobs/{id:guid}/approve", async (
    Guid id,
    HttpContext httpContext,
    AppDbContext db,
    IJobQueue queue,
    IJobStatusNotifier notifier,
    CancellationToken cancellationToken) =>
{
    var approverId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(approverId))
    {
        return Results.Unauthorized();
    }

    var job = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (job is null)
    {
        return Results.NotFound();
    }

    if (job.Status != JobStatus.PendingApproval)
    {
        return Results.Conflict(new { error = "Job is not waiting for approval." });
    }

    var sequence = await db.AgentEvents
        .Where(x => x.JobId == job.Id)
        .Select(x => (int?)x.Sequence)
        .MaxAsync(cancellationToken) ?? 0;

    job.ApprovalGranted = true;
    job.ApprovalGrantedBy = approverId.Trim();
    job.ApprovalGrantedAtUtc = DateTime.UtcNow;
    job.Status = JobStatus.Pending;
    job.LastError = null;

    db.AgentEvents.Add(new AgentEvent
    {
        JobId = job.Id,
        Sequence = ++sequence,
        Stage = AgentStage.DryRun,
        Stream = "system",
        Message = $"Delegated approval granted by {job.ApprovalGrantedBy}."
    });

    await db.SaveChangesAsync(cancellationToken);
    await notifier.PublishStatusAsync(job.Id, job.Status, job.CurrentStage, cancellationToken);
    await queue.EnqueueAsync(job.Id, cancellationToken);

    return Results.Accepted($"/jobs/{job.Id}", new
    {
        job.Id,
        status = job.Status.ToString(),
        currentStage = job.CurrentStage.ToString()
    });
})
.RequireAuthorization("CanApproveInfrastructure");

app.MapGet("/jobs", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var jobs = await db.Jobs
        .AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(50)
        .Select(x => new
        {
            x.Id,
            x.UserId,
            x.EnvironmentId,
            x.Status,
            x.CurrentStage,
            x.CreatedAtUtc,
            x.CompletedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(jobs);
});

app.MapGet("/jobs/{id:guid}", async (Guid id, AppDbContext db, CancellationToken cancellationToken) =>
{
    var job = await db.Jobs
        .AsNoTracking()
        .Include(x => x.Events.OrderBy(y => y.Sequence))
        .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    if (job is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new JobDetailsResponse(
        job.Id,
        job.TemplateId,
        job.UserId,
        job.EnvironmentId,
        job.Status.ToString(),
        job.CurrentStage.ToString(),
        JsonSerializer.Deserialize<JsonElement>(job.ParametersJson),
        job.CreatedAtUtc,
        job.StartedAtUtc,
        job.CompletedAtUtc,
        job.LastError,
        job.ApprovalRequestedAtUtc,
        job.ApprovalGranted,
        job.ApprovalGrantedBy,
        job.ApprovalGrantedAtUtc,
        job.Events.Select(e => new JobEventResponse(
            e.Sequence,
            e.Stage.ToString(),
            e.Stream,
            e.Message,
            e.TimestampUtc)).ToArray()));
});

app.MapHub<JobStatusHub>("/hubs/jobs");

app.Run();

static void EnsureSqliteCompatibility(AppDbContext db)
{
    if (!db.Database.IsSqlite())
    {
        return;
    }

    using var connection = db.Database.GetDbConnection();
    connection.Open();

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA table_info('Jobs');";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    ExecuteNonQueryIfMissing(
        connection,
        existingColumns,
        "ApprovalRequestedAtUtc",
        "ALTER TABLE Jobs ADD COLUMN ApprovalRequestedAtUtc TEXT NULL;");
    ExecuteNonQueryIfMissing(
        connection,
        existingColumns,
        "ApprovalGranted",
        "ALTER TABLE Jobs ADD COLUMN ApprovalGranted INTEGER NOT NULL DEFAULT 0;");
    ExecuteNonQueryIfMissing(
        connection,
        existingColumns,
        "ApprovalGrantedBy",
        "ALTER TABLE Jobs ADD COLUMN ApprovalGrantedBy TEXT NULL;");
    ExecuteNonQueryIfMissing(
        connection,
        existingColumns,
        "ApprovalGrantedAtUtc",
        "ALTER TABLE Jobs ADD COLUMN ApprovalGrantedAtUtc TEXT NULL;");

    using var indexCommand = connection.CreateCommand();
    indexCommand.CommandText = """
        CREATE INDEX IF NOT EXISTS IX_Jobs_Status_CreatedAtUtc
        ON Jobs(Status, CreatedAtUtc);
        """;
    indexCommand.ExecuteNonQuery();
}

static void ExecuteNonQueryIfMissing(
    DbConnection connection,
    ISet<string> existingColumns,
    string columnName,
    string sql)
{
    if (existingColumns.Contains(columnName))
    {
        return;
    }

    using var alterTable = connection.CreateCommand();
    alterTable.CommandText = sql;
    alterTable.ExecuteNonQuery();
}

public partial class Program;
