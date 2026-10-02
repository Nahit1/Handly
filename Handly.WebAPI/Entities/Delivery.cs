namespace Handly.WebAPI.Entities;

public class Delivery
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EndpointId { get; set; }
    public string? ExternalId { get; set; }
    public string? IdempotencyKey { get; set; }
    public required string Payload { get; set; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public WebhookEndpoint Endpoint { get; set; } = null!;
    public ICollection<DeliveryAttempt> Attempts { get; set; } = [];
}
