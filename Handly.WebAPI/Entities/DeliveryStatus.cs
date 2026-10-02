namespace Handly.WebAPI.Entities;

public static class DeliveryStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string RetryScheduled = "RetryScheduled";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
}
