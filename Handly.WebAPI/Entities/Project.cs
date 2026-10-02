namespace Handly.WebAPI.Entities;

public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Environment { get; set; }
    public string? ApiKeyHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<WebhookEndpoint> Endpoints { get; set; } = [];
    public ICollection<Delivery> Deliveries { get; set; } = [];
    public ICollection<ProjectMember> Members { get; set; } = [];
}
