namespace Handly.WebAPI.Entities;

public class DeliveryAttempt
{
    public Guid Id { get; set; }
    public Guid DeliveryId { get; set; }
    public int AttemptNumber { get; set; }
    public int? HttpStatusCode { get; set; }
    public long? DurationMs { get; set; }
    public string? ResponseBody { get; set; }
    public string? ResponseHeaders { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Delivery Delivery { get; set; } = null!;
}
