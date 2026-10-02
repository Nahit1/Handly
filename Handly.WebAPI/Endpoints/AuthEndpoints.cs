using Handly.WebAPI.Auth;
using Handly.WebAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, HandlyDbContext db, JwtTokenService jwtService, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Results.Unauthorized();

            var token = jwtService.GenerateToken(user);

            return Results.Ok(new LoginResponse(token, user.Email, user.FullName, user.Role, user.ProjectId));
        });

        group.MapPost("/register", async (RegisterRequest request, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            var emailExists = await db.Users.AnyAsync(u => u.Email == request.Email, ct);
            if (emailExists)
                return Results.Conflict(new { error = "Email already registered." });

            var user = new Entities.User
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FullName = request.FullName,
                Role = Entities.UserRole.Admin,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/auth/me", new UserResponse(user.Id, user.Email, user.FullName, user.Role));
        });
    }
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string FullName);
public record LoginResponse(string Token, string Email, string FullName, string Role, Guid ProjectId);
public record UserResponse(Guid Id, string Email, string FullName, string Role);
