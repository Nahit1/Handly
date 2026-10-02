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

        // Kullanıcının projelerini listele (JWT)
        group.MapGet("/", async (HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var userId = context.GetUserId();

            var projects = await db.ProjectMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => new ProjectListResponse(m.ProjectId, m.Project.Name, m.Project.Slug, m.Project.Environment, m.Role, m.Project.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(projects);
        });

        // Yeni proje oluştur (JWT) — oluşturan Owner olur
        group.MapPost("/", async (CreateProjectRequest request, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var userId = context.GetUserId();

            var slugExists = await db.Projects.AnyAsync(p => p.Slug == request.Slug, ct);
            if (slugExists)
                return Results.Conflict(new { error = "A project with this slug already exists." });

            var apiKey = ApiKeyService.GenerateApiKey();
            var apiKeyHash = ApiKeyService.HashApiKey(apiKey);
            var now = DateTime.UtcNow;

            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Slug = request.Slug,
                Environment = request.Environment,
                ApiKeyHash = apiKeyHash,
                CreatedAt = now,
                UpdatedAt = now
            };

            var membership = new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                UserId = userId,
                Role = ProjectRole.Owner,
                CreatedAt = now
            };

            db.Projects.Add(project);
            db.ProjectMembers.Add(membership);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/projects/{project.Id}",
                new ProjectCreatedResponse(project.Id, project.Name, project.Slug, project.Environment, apiKey, project.CreatedAt));
        });

        // Seçili projenin bilgisi (JWT + X-Project-Id veya API Key)
        group.MapGet("/current", (HttpContext context) =>
        {
            var project = context.GetProject();
            return Results.Ok(new ProjectResponse(project.Id, project.Name, project.Slug, project.Environment, project.CreatedAt));
        });

        // Proje üyelerini listele
        group.MapGet("/current/members", async (HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            var members = await db.ProjectMembers
                .AsNoTracking()
                .Where(m => m.ProjectId == projectId)
                .Select(m => new MemberResponse(m.UserId, m.User.Email, m.User.FullName, m.Role, m.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(members);
        });

        // Projeye üye davet et
        group.MapPost("/current/members", async (InviteMemberRequest request, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            var user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

            if (user is null)
                return Results.NotFound(new { error = "User not found with this email." });

            var alreadyMember = await db.ProjectMembers
                .AnyAsync(m => m.ProjectId == projectId && m.UserId == user.Id, ct);

            if (alreadyMember)
                return Results.Conflict(new { error = "User is already a member of this project." });

            var membership = new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = user.Id,
                Role = request.Role ?? ProjectRole.Viewer,
                CreatedAt = DateTime.UtcNow
            };

            db.ProjectMembers.Add(membership);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/projects/current/members",
                new MemberResponse(user.Id, user.Email, user.FullName, membership.Role, membership.CreatedAt));
        });
    }

    public static Project GetProject(this HttpContext context)
    {
        if (context.Items.TryGetValue("Project", out var proj) && proj is Project project)
            return project;

        throw new InvalidOperationException("No project in context. Provide X-Project-Id header or X-Api-Key.");
    }

    public static Guid GetProjectId(this HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectId", out var id) && id is Guid projectId)
            return projectId;

        throw new InvalidOperationException("No project in context. Provide X-Project-Id header or X-Api-Key.");
    }
}

public record CreateProjectRequest(string Name, string Slug, string? Environment);
public record ProjectResponse(Guid Id, string Name, string Slug, string? Environment, DateTime CreatedAt);
public record ProjectCreatedResponse(Guid Id, string Name, string Slug, string? Environment, string ApiKey, DateTime CreatedAt);
public record ProjectListResponse(Guid Id, string Name, string Slug, string? Environment, string Role, DateTime CreatedAt);
public record InviteMemberRequest(string Email, string? Role);
public record MemberResponse(Guid UserId, string Email, string FullName, string Role, DateTime JoinedAt);
