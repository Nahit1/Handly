using System.IdentityModel.Tokens.Jwt;
using Handly.WebAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Auth;

public class AuthMiddleware(RequestDelegate next)
{
    private const string ApiKeyHeader = "X-Api-Key";

    private static readonly HashSet<string> PublicPaths =
    [
        "/api/auth/login",
        "/api/auth/register"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip non-api routes (OpenAPI, static files, etc.)
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Public endpoints — no auth needed
        if (PublicPaths.Contains(path.TrimEnd('/').ToLowerInvariant()))
        {
            await next(context);
            return;
        }

        // --- Try API Key (machine-to-machine) ---
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

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
            return;
        }

        // --- Try JWT Bearer (dashboard users) ---
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                context.Items["UserId"] = userId;
                context.Items["AuthType"] = "JWT";

                // Project-scoped endpoints need X-Project-Id header
                if (context.Request.Headers.TryGetValue("X-Project-Id", out var projectIdHeader)
                    && Guid.TryParse(projectIdHeader, out var projectId))
                {
                    var db = context.RequestServices.GetRequiredService<HandlyDbContext>();

                    // Verify user is a member of this project
                    var membership = await db.ProjectMembers.AsNoTracking()
                        .FirstOrDefaultAsync(m => m.UserId == userId && m.ProjectId == projectId, context.RequestAborted);

                    if (membership is not null)
                    {
                        var project = await db.Projects.AsNoTracking()
                            .FirstOrDefaultAsync(p => p.Id == projectId, context.RequestAborted);

                        if (project is not null)
                        {
                            context.Items["Project"] = project;
                            context.Items["ProjectId"] = project.Id;
                            context.Items["MemberRole"] = membership.Role;
                        }
                    }
                }

                await next(context);
                return;
            }
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized. Provide X-Api-Key header or Bearer token." });
    }
}
