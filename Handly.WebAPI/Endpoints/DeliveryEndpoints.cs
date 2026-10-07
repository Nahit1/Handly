using System.Text.Json;
using Handly.WebAPI.Data;
using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Endpoints;

public static class DeliveryEndpoints
{
    public static void MapDeliveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/deliveries").WithTags("Deliveries");

        group.MapPost("/", async (CreateDeliveryRequest request, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            // Endpoint must belong to the authenticated project
            var endpoint = await db.Endpoints.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == request.EndpointId && e.ProjectId == projectId, ct);

            if (endpoint is null)
                return Results.BadRequest(new { error = "Endpoint not found or does not belong to this project." });

            // Idempotency check
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing = await db.Deliveries.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.ProjectId == projectId && d.IdempotencyKey == request.IdempotencyKey, ct);

                if (existing is not null)
                    return Results.Ok(new DeliveryResponse(existing.Id, existing.Status, existing.CreatedAt));
            }

            var now = DateTime.UtcNow;
            var delivery = new Delivery
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                EndpointId = endpoint.Id,
                ExternalId = request.ExternalId,
                IdempotencyKey = request.IdempotencyKey,
                Payload = request.Payload is not null ? request.Payload.Value.GetRawText() : endpoint.DefaultPayload ?? "{}",
                Status = DeliveryStatus.Pending,
                AttemptCount = 0,
                NextAttemptAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Deliveries.Add(delivery);
            await db.SaveChangesAsync(ct);

            return Results.Accepted($"/api/deliveries/{delivery.Id}",
                new DeliveryResponse(delivery.Id, delivery.Status, delivery.CreatedAt));
        });

        group.MapGet("/{id:guid}", async (Guid id, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            var delivery = await db.Deliveries.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.ProjectId == projectId, ct);

            if (delivery is null) return Results.NotFound();

            return Results.Ok(new DeliveryDetailResponse(
                delivery.Id, delivery.EndpointId, delivery.Status, delivery.AttemptCount,
                delivery.NextAttemptAt, delivery.LastAttemptAt, delivery.CompletedAt,
                delivery.CreatedAt));
        });

        group.MapGet("/{id:guid}/attempts", async (Guid id, HandlyDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var projectId = context.GetProjectId();

            // Verify delivery belongs to this project
            var deliveryExists = await db.Deliveries.AnyAsync(d => d.Id == id && d.ProjectId == projectId, ct);
            if (!deliveryExists) return Results.NotFound();

            var attempts = await db.DeliveryAttempts
                .AsNoTracking()
                .Where(a => a.DeliveryId == id)
                .OrderBy(a => a.AttemptNumber)
                .Select(a => new DeliveryAttemptResponse(
                    a.Id, a.AttemptNumber, a.HttpStatusCode, a.DurationMs,
                    a.ErrorType, a.ErrorMessage, a.StartedAt, a.CompletedAt))
                .ToListAsync(ct);

            return Results.Ok(attempts);
        });
    }
}

public record CreateDeliveryRequest(Guid EndpointId, string? ExternalId, string? IdempotencyKey, JsonElement? Payload);
public record DeliveryResponse(Guid DeliveryId, string Status, DateTime CreatedAt);
public record DeliveryDetailResponse(Guid Id, Guid EndpointId, string Status, int AttemptCount, DateTime? NextAttemptAt, DateTime? LastAttemptAt, DateTime? CompletedAt, DateTime CreatedAt);
public record DeliveryAttemptResponse(Guid Id, int AttemptNumber, int? HttpStatusCode, long? DurationMs, string? ErrorType, string? ErrorMessage, DateTime StartedAt, DateTime? CompletedAt);
