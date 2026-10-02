namespace Handly.WebAPI.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
}

public static class UserRole
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";
}
