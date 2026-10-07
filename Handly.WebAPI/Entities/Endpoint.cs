namespace Handly.WebAPI.Entities;

public class WebhookEndpoint
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public required string HttpMethod { get; set; }
    public string? Headers { get; set; }
    public string? DefaultPayload { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public ICollection<Delivery> Deliveries { get; set; } = [];
}
