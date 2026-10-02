using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Handly.WebAPI.Data;
using Handly.WebAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handly.WebAPI.Workers;

public class DeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 20;

    private static readonly int[] BackoffSeconds = [0, 10, 30, 120, 600, 1800, 3600];
    private static readonly HashSet<int> NonRetryableStatusCodes = [400, 401, 403, 404, 405, 409, 410, 422];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DeliveryWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);

                if (processedCount == 0)
                    await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in delivery worker loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("DeliveryWorker stopped");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HandlyDbContext>();

        // Phase 1: Claim deliveries with short transaction
        var claimed = await ClaimDeliveriesAsync(db, ct);
        if (claimed.Count == 0) return 0;

        logger.LogInformation("Claimed {Count} deliveries for processing", claimed.Count);

        // Phase 2: Process each delivery (outside the claim transaction)
        foreach (var item in claimed)
        {
            await ProcessDeliveryAsync(db, item, ct);
        }

        return claimed.Count;
    }

    private static async Task<List<ClaimedDelivery>> ClaimDeliveriesAsync(HandlyDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Use raw SQL for FOR UPDATE SKIP LOCKED
        var deliveries = await db.Deliveries
            .FromSqlRaw("""
                SELECT * FROM deliveries
                WHERE status IN ('Pending', 'RetryScheduled')
                  AND (next_attempt_at IS NULL OR next_attempt_at <= {0})
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT {1}
                """, now, BatchSize)
            .ToListAsync(ct);

        if (deliveries.Count == 0) return [];

        foreach (var d in deliveries)
        {
            d.Status = DeliveryStatus.Processing;
            d.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        // Load endpoint info for each delivery
        var endpointIds = deliveries.Select(d => d.EndpointId).Distinct().ToList();
        var endpoints = await db.Endpoints
            .AsNoTracking()
            .Where(e => endpointIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        return deliveries
            .Where(d => endpoints.ContainsKey(d.EndpointId))
            .Select(d => new ClaimedDelivery(d.Id, d.EndpointId, d.Payload, d.AttemptCount, endpoints[d.EndpointId]))
            .ToList();
    }

    private async Task ProcessDeliveryAsync(HandlyDbContext db, ClaimedDelivery item, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var attemptNumber = item.AttemptCount + 1;
        var attempt = new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            DeliveryId = item.DeliveryId,
            AttemptNumber = attemptNumber,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            var client = httpClientFactory.CreateClient("DeliveryClient");
            client.Timeout = TimeSpan.FromSeconds(item.Endpoint.TimeoutSeconds);

            using var request = BuildHttpRequest(item);
            using var response = await client.SendAsync(request, ct);

            sw.Stop();
            attempt.HttpStatusCode = (int)response.StatusCode;
            attempt.DurationMs = sw.ElapsedMilliseconds;
            attempt.CompletedAt = DateTime.UtcNow;

            // Truncate response body to 10KB
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            attempt.ResponseBody = responseBody.Length > 10240 ? responseBody[..10240] : responseBody;

            var delivery = await db.Deliveries.FindAsync([item.DeliveryId], ct);
            if (delivery is null) return;

            var now = DateTime.UtcNow;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = DeliveryStatus.Delivered;
                delivery.CompletedAt = now;
                logger.LogInformation("Delivery {Id} succeeded with HTTP {Status}", item.DeliveryId, attempt.HttpStatusCode);
            }
            else if (ShouldRetry((int)response.StatusCode, attemptNumber, item.Endpoint.MaxAttempts))
            {
                var delay = GetBackoffDelay(attemptNumber);
                delivery.Status = DeliveryStatus.RetryScheduled;
                delivery.NextAttemptAt = now.Add(delay);
                logger.LogWarning("Delivery {Id} got HTTP {Status}, retry #{Attempt} scheduled in {Delay}s",
                    item.DeliveryId, attempt.HttpStatusCode, attemptNumber, delay.TotalSeconds);
            }
            else
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.CompletedAt = now;
                logger.LogWarning("Delivery {Id} failed permanently with HTTP {Status}", item.DeliveryId, attempt.HttpStatusCode);
            }

            delivery.AttemptCount = attemptNumber;
            delivery.LastAttemptAt = now;
            delivery.UpdatedAt = now;

            db.DeliveryAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            attempt.DurationMs = sw.ElapsedMilliseconds;
            attempt.CompletedAt = DateTime.UtcNow;
            attempt.ErrorType = ex.GetType().Name;
            attempt.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            logger.LogWarning(ex, "Delivery {Id} attempt {Attempt} threw exception", item.DeliveryId, attemptNumber);

            try
            {
                var delivery = await db.Deliveries.FindAsync([item.DeliveryId], ct);
                if (delivery is null) return;

                var now = DateTime.UtcNow;

                if (attemptNumber < item.Endpoint.MaxAttempts)
                {
                    var delay = GetBackoffDelay(attemptNumber);
                    delivery.Status = DeliveryStatus.RetryScheduled;
                    delivery.NextAttemptAt = now.Add(delay);
                }
                else
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.CompletedAt = now;
                }

                delivery.AttemptCount = attemptNumber;
                delivery.LastAttemptAt = now;
                delivery.UpdatedAt = now;

                db.DeliveryAttempts.Add(attempt);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception innerEx)
            {
                logger.LogError(innerEx, "Failed to persist attempt result for delivery {Id}", item.DeliveryId);
            }
        }
    }

    private static HttpRequestMessage BuildHttpRequest(ClaimedDelivery item)
    {
        var method = new HttpMethod(item.Endpoint.HttpMethod);
        var request = new HttpRequestMessage(method, item.Endpoint.Url);

        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent(item.Payload, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        }

        // Apply endpoint-level headers
        if (!string.IsNullOrWhiteSpace(item.Endpoint.Headers))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(item.Endpoint.Headers);
                if (headers is not null)
                {
                    foreach (var (key, value) in headers)
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }
            catch
            {
                // Skip malformed headers
            }
        }

        return request;
    }

    private static bool ShouldRetry(int statusCode, int attemptNumber, int maxAttempts)
    {
        if (attemptNumber >= maxAttempts) return false;
        if (NonRetryableStatusCodes.Contains(statusCode)) return false;
        return statusCode is >= 408 or >= 500;
    }

    private static TimeSpan GetBackoffDelay(int attemptNumber)
    {
        var index = Math.Min(attemptNumber, BackoffSeconds.Length - 1);
        return TimeSpan.FromSeconds(BackoffSeconds[index]);
    }

    private record ClaimedDelivery(Guid DeliveryId, Guid EndpointId, string Payload, int AttemptCount, WebhookEndpoint Endpoint);
}
