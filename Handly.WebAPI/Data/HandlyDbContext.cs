using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Data;

public class HandlyDbContext(DbContextOptions<HandlyDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HandlyDbContext).Assembly);
    }
}
