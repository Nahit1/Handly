using Handly.WebAPI.Data;
using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace Handly.WebAPI.Endpoints;

public static class EndpointEndpoints
{
    public static void MapEndpointEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/endpoints").WithTags("Endpoints");

        group.MapPost("/", async (CreateEndpointRequest request, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();
            var headers = BuildAuthenticationHeaders(request);
            if (headers.IsError)
                return Results.BadRequest(new { error = headers.Error });

            var endpoint = new WebhookEndpoint
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = request.Name,
                Url = request.Url,
                HttpMethod = request.HttpMethod ?? "POST",
                DefaultPayload = request.DefaultPayload,
                Headers = headers.Value,
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
                endpoint.HttpMethod, endpoint.DefaultPayload, request.AuthenticationType ?? "None", endpoint.Headers is not null, endpoint.TimeoutSeconds, endpoint.MaxAttempts,
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
                endpoint.HttpMethod, endpoint.DefaultPayload, "None", endpoint.Headers is not null, endpoint.TimeoutSeconds, endpoint.MaxAttempts,
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
                    e.HttpMethod, e.DefaultPayload, "None", e.Headers != null, e.TimeoutSeconds, e.MaxAttempts,
                    e.IsActive, e.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(endpoints);
        });
    }

    private static AuthHeaders BuildAuthenticationHeaders(CreateEndpointRequest request)
    {
        var type = request.AuthenticationType?.Trim().ToLowerInvariant() ?? "none";
        return type switch
        {
            "none" => new(null),
            "bearer" when !string.IsNullOrWhiteSpace(request.BearerToken) => new(JsonSerializer.Serialize(new Dictionary<string, string> { ["Authorization"] = $"Bearer {request.BearerToken.Trim()}" })),
            "basic" when !string.IsNullOrWhiteSpace(request.BasicUsername) && request.BasicPassword is not null => new(JsonSerializer.Serialize(new Dictionary<string, string> { ["Authorization"] = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{request.BasicUsername}:{request.BasicPassword}"))}" })),
            "bearer" => new("Bearer token is required."),
            "basic" => new("Username and password are required for Basic authentication."),
            _ => new("Unsupported authentication type.")
        };
    }

    private record AuthHeaders(string? Value, string? Error = null)
    {
        public bool IsError => Error is not null;
    }
}

public record CreateEndpointRequest(string Name, string Url, string? HttpMethod, string? DefaultPayload, string? AuthenticationType, string? BearerToken, string? BasicUsername, string? BasicPassword, int? TimeoutSeconds, int? MaxAttempts);
public record EndpointResponse(Guid Id, string Name, string Url, string HttpMethod, string? DefaultPayload, string AuthenticationType, bool HasAuthentication, int TimeoutSeconds, int MaxAttempts, bool IsActive, DateTime CreatedAt);
