using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Handly.WebAPI.Configurations;

public class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.ProjectId).HasColumnName("project_id");
        builder.Property(d => d.EndpointId).HasColumnName("endpoint_id");
        builder.Property(d => d.ExternalId).HasColumnName("external_id").HasMaxLength(500);
        builder.Property(d => d.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(500);
        builder.Property(d => d.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
        builder.Property(d => d.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        builder.Property(d => d.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(d => d.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(d => d.CompletedAt).HasColumnName("completed_at");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(d => d.Project)
            .WithMany(p => p.Deliveries)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Endpoint)
            .WithMany(e => e.Deliveries)
            .HasForeignKey(d => d.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);

        // Worker lookup index
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt })
            .HasDatabaseName("ix_deliveries_worker");

        // Idempotency: unique per project, only when key is not null
        builder.HasIndex(d => new { d.ProjectId, d.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_deliveries_project_idempotency")
            .HasFilter("idempotency_key IS NOT NULL");
    }
}
