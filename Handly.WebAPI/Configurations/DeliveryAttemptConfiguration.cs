using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Handly.WebAPI.Configurations;

public class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.DeliveryId).HasColumnName("delivery_id");
        builder.Property(a => a.AttemptNumber).HasColumnName("attempt_number");
        builder.Property(a => a.HttpStatusCode).HasColumnName("http_status_code");
        builder.Property(a => a.DurationMs).HasColumnName("duration_ms");
        builder.Property(a => a.ResponseBody).HasColumnName("response_body");
        builder.Property(a => a.ResponseHeaders).HasColumnName("response_headers").HasColumnType("jsonb");
        builder.Property(a => a.ErrorType).HasColumnName("error_type").HasMaxLength(200);
        builder.Property(a => a.ErrorMessage).HasColumnName("error_message");
        builder.Property(a => a.StartedAt).HasColumnName("started_at");
        builder.Property(a => a.CompletedAt).HasColumnName("completed_at");

        builder.HasOne(a => a.Delivery)
            .WithMany(d => d.Attempts)
            .HasForeignKey(a => a.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.DeliveryId, a.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("ux_delivery_attempts_number");
    }
}
