namespace Handly.WebAPI.Entities;

public class ProjectMember
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public User User { get; set; } = null!;
}

public static class ProjectRole
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";
}
