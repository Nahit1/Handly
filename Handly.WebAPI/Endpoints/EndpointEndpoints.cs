using Handly.WebAPI.Data;
using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Endpoints;

public static class EndpointEndpoints
{
    public static void MapEndpointEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/endpoints").WithTags("Endpoints");

        group.MapPost("/", async (CreateEndpointRequest request, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            var endpoint = new WebhookEndpoint
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = request.Name,
                Url = request.Url,
                HttpMethod = request.HttpMethod ?? "POST",
                TimeoutSeconds = request.TimeoutSeconds ?? 30,
                MaxAttempts = request.MaxAttempts ?? 5,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Endpoints.Add(endpoint);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/endpoints/{endpoint.Id}", new EndpointResponse(
                endpoint.Id, endpoint.Name, endpoint.Url,
                endpoint.HttpMethod, endpoint.TimeoutSeconds, endpoint.MaxAttempts,
                endpoint.IsActive, endpoint.CreatedAt));
        });

        group.MapGet("/{id:guid}", async (Guid id, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();
            var endpoint = await db.Endpoints.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.ProjectId == projectId, ct);

            if (endpoint is null) return Results.NotFound();

            return Results.Ok(new EndpointResponse(
                endpoint.Id, endpoint.Name, endpoint.Url,
                endpoint.HttpMethod, endpoint.TimeoutSeconds, endpoint.MaxAttempts,
                endpoint.IsActive, endpoint.CreatedAt));
        });

        group.MapGet("/", async (HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            var endpoints = await db.Endpoints
                .AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .Select(e => new EndpointResponse(
                    e.Id, e.Name, e.Url,
                    e.HttpMethod, e.TimeoutSeconds, e.MaxAttempts,
                    e.IsActive, e.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(endpoints);
        });
    }
}

public record CreateEndpointRequest(string Name, string Url, string? HttpMethod, int? TimeoutSeconds, int? MaxAttempts);
public record EndpointResponse(Guid Id, string Name, string Url, string HttpMethod, int TimeoutSeconds, int MaxAttempts, bool IsActive, DateTime CreatedAt);
