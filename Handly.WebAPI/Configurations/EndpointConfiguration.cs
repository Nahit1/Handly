using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Handly.WebAPI.Configurations;

public class EndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("endpoints");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ProjectId).HasColumnName("project_id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Url).HasColumnName("url").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.HttpMethod).HasColumnName("http_method").HasMaxLength(10).IsRequired();
        builder.Property(e => e.Headers).HasColumnName("headers").HasColumnType("jsonb");
        builder.Property(e => e.DefaultPayload).HasColumnName("default_payload").HasColumnType("jsonb");
        builder.Property(e => e.TimeoutSeconds).HasColumnName("timeout_seconds").HasDefaultValue(30);
        builder.Property(e => e.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5);
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(e => e.Project)
            .WithMany(p => p.Endpoints)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ProjectId).HasDatabaseName("ix_endpoints_project");
    }
}
