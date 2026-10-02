using System.IdentityModel.Tokens.Jwt;
using Handly.WebAPI.Auth;
using Handly.WebAPI.Data;
using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, HandlyDbContext db, JwtTokenService jwtService, CancellationToken ct) =>
        {
            var emailExists = await db.Users.AnyAsync(u => u.Email == request.Email, ct);
            if (emailExists)
                return Results.Conflict(new { error = "Email already registered." });

            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FullName = request.FullName,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            var token = jwtService.GenerateToken(user);

            return Results.Created("/api/auth/me",
                new AuthResponse(token, user.Id, user.Email, user.FullName));
        });

        group.MapPost("/login", async (LoginRequest request, HandlyDbContext db, JwtTokenService jwtService, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Results.Unauthorized();

            var token = jwtService.GenerateToken(user);

            // Kullanıcının üye olduğu projeleri de dön
            var projects = await db.ProjectMembers
                .AsNoTracking()
                .Where(m => m.UserId == user.Id)
                .Select(m => new ProjectMembershipResponse(m.ProjectId, m.Project.Name, m.Project.Slug, m.Role))
                .ToListAsync(ct);

            return Results.Ok(new LoginResponse(token, user.Id, user.Email, user.FullName, projects));
        });

        group.MapGet("/me", (HttpContext context) =>
        {
            var userId = context.GetUserId();
            return Results.Ok(new { userId });
        });
    }

    public static Guid GetUserId(this HttpContext context)
    {
        if (context.Items.TryGetValue("UserId", out var id) && id is Guid userId)
            return userId;

        // Fallback: API Key auth doesn't have a user
        throw new InvalidOperationException("No authenticated user. This endpoint requires JWT authentication.");
    }
}

public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, Guid UserId, string Email, string FullName);
public record LoginResponse(string Token, Guid UserId, string Email, string FullName, List<ProjectMembershipResponse> Projects);
public record ProjectMembershipResponse(Guid ProjectId, string Name, string Slug, string Role);
