using Handly.WebAPI.Auth;
using Handly.WebAPI.Data;
using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");

        group.MapPost("/", async (CreateProjectRequest request, HandlyDbContext db, CancellationToken ct) =>
        {
            var apiKey = ApiKeyService.GenerateApiKey();
            var apiKeyHash = ApiKeyService.HashApiKey(apiKey);

            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Slug = request.Slug,
                Environment = request.Environment,
                ApiKeyHash = apiKeyHash,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/projects/{project.Id}",
                new ProjectCreatedResponse(project.Id, project.Name, project.Slug, project.Environment, apiKey, project.CreatedAt));
        });

        group.MapGet("/me", (HttpContext context) =>
        {
            var project = context.GetProject();
            return Results.Ok(new ProjectResponse(project.Id, project.Name, project.Slug, project.Environment, project.CreatedAt));
        });
    }

    public static Project GetProject(this HttpContext context)
    {
        return (Project)context.Items["Project"]!;
    }

    public static Guid GetProjectId(this HttpContext context)
    {
        return (Guid)context.Items["ProjectId"]!;
    }
}

public record CreateProjectRequest(string Name, string Slug, string? Environment);
public record ProjectResponse(Guid Id, string Name, string Slug, string? Environment, DateTime CreatedAt);
public record ProjectCreatedResponse(Guid Id, string Name, string Slug, string? Environment, string ApiKey, DateTime CreatedAt);
