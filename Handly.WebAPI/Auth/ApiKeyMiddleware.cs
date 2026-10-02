using System.Security.Claims;
using Handly.WebAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Auth;

public class ApiKeyMiddleware(RequestDelegate next)
{
    private const string ApiKeyHeader = "X-Api-Key";

    private static readonly HashSet<string> PublicPaths =
    [
        "/api/auth/login"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip non-api routes
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Public endpoints (no auth needed)
        if (PublicPaths.Contains(path.TrimEnd('/').ToLowerInvariant()))
        {
            await next(context);
            return;
        }

        // Project creation doesn't require auth
        if (path.Equals("/api/projects", StringComparison.OrdinalIgnoreCase)
            && context.Request.Method == HttpMethods.Post)
        {
            await next(context);
            return;
        }

        // Try JWT Bearer first (dashboard users)
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var projectIdClaim = context.User.FindFirstValue("projectId");
            if (Guid.TryParse(projectIdClaim, out var projectId))
            {
                var db = context.RequestServices.GetRequiredService<HandlyDbContext>();
                var project = await db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == projectId, context.RequestAborted);

                if (project is not null)
                {
                    context.Items["Project"] = project;
                    context.Items["ProjectId"] = project.Id;
                    context.Items["AuthType"] = "JWT";
                    await next(context);
                    return;
                }
            }
        }

        // Try API Key (machine-to-machine)
        if (context.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValue)
            && !string.IsNullOrWhiteSpace(apiKeyValue))
        {
            var hash = ApiKeyService.HashApiKey(apiKeyValue!);
            var db = context.RequestServices.GetRequiredService<HandlyDbContext>();

            var project = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ApiKeyHash == hash, context.RequestAborted);

            if (project is not null)
            {
                context.Items["Project"] = project;
                context.Items["ProjectId"] = project.Id;
                context.Items["AuthType"] = "ApiKey";
                await next(context);
                return;
            }
        }

        // Register requires API key (project must exist)
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized. Provide X-Api-Key header or Bearer token." });
    }
}
