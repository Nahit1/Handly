using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory request log
var requestLog = new ConcurrentQueue<RequestLogEntry>();

// Default: %40 failure rate
var failureRate = 40;

// ============================================================
// WEBHOOK ENDPOINTS — Handly bu endpoint'lere delivery yapar
// ============================================================

// POST /webhook/leads — Lead webhook (configurable failure)
app.MapPost("/webhook/leads", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var delay = Random.Shared.Next(50, 500); // 50-500ms simulated latency
    await Task.Delay(delay);

    var shouldFail = Random.Shared.Next(100) < failureRate;
    var statusCode = shouldFail ? PickFailureStatus() : 200;

    LogRequest(requestLog, "POST /webhook/leads", statusCode, delay, body);

    if (statusCode == 200)
    {
        return Results.Ok(new { status = "received", leadId = ExtractField(body, "leadId"), processedAt = DateTime.UtcNow });
    }

    return Results.StatusCode(statusCode);
});

// POST /webhook/notifications — Notification webhook
app.MapPost("/webhook/notifications", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var delay = Random.Shared.Next(100, 800);
    await Task.Delay(delay);

    var shouldFail = Random.Shared.Next(100) < failureRate;
    var statusCode = shouldFail ? PickFailureStatus() : 200;

    LogRequest(requestLog, "POST /webhook/notifications", statusCode, delay, body);

    return statusCode == 200
        ? Results.Ok(new { status = "delivered", timestamp = DateTime.UtcNow })
        : Results.StatusCode(statusCode);
});

// POST /webhook/crm — CRM sync webhook
app.MapPost("/webhook/crm", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var delay = Random.Shared.Next(200, 1500); // CRM is slow
    await Task.Delay(delay);

    var shouldFail = Random.Shared.Next(100) < failureRate;
    var statusCode = shouldFail ? PickFailureStatus() : 201;

    LogRequest(requestLog, "POST /webhook/crm", statusCode, delay, body);

    return statusCode == 201
        ? Results.Json(new { status = "synced", crmId = $"CRM-{Random.Shared.Next(10000, 99999)}", timestamp = DateTime.UtcNow }, statusCode: 201)
        : Results.StatusCode(statusCode);
});

// POST /webhook/always-ok — Her zaman 200 doner (happy path testi)
app.MapPost("/webhook/always-ok", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    await Task.Delay(Random.Shared.Next(10, 100));

    LogRequest(requestLog, "POST /webhook/always-ok", 200, 0, body);

    return Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow });
});

// POST /webhook/always-fail — Her zaman 500 doner (retry testi)
app.MapPost("/webhook/always-fail", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    await Task.Delay(Random.Shared.Next(50, 200));

    LogRequest(requestLog, "POST /webhook/always-fail", 500, 0, body);

    return Results.StatusCode(500);
});

// POST /webhook/slow — 5 saniye gecikme (timeout testi)
app.MapPost("/webhook/slow", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    await Task.Delay(5000);

    LogRequest(requestLog, "POST /webhook/slow", 200, 5000, body);

    return Results.Ok(new { status = "finally done", timestamp = DateTime.UtcNow });
});

// POST /webhook/flaky — Ilk 2 istek fail, 3. basarili (retry senaryosu)
var flakyCounter = new ConcurrentDictionary<string, int>();
app.MapPost("/webhook/flaky", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var key = ExtractField(body, "idempotencyKey") ?? ExtractField(body, "leadId") ?? "default";
    var count = flakyCounter.AddOrUpdate(key, 1, (_, c) => c + 1);

    await Task.Delay(Random.Shared.Next(50, 300));

    var statusCode = count <= 2 ? 503 : 200;
    LogRequest(requestLog, $"POST /webhook/flaky (attempt #{count})", statusCode, 0, body);

    return statusCode == 200
        ? Results.Ok(new { status = "success_after_retries", attempt = count, timestamp = DateTime.UtcNow })
        : Results.StatusCode(statusCode);
});

// ============================================================
// ADMIN ENDPOINTS — Failure rate ve log yonetimi
// ============================================================

// GET /admin/config — Mevcut konfigurasyonu gor
app.MapGet("/admin/config", () => Results.Ok(new { failureRate, description = $"%{failureRate} olasilikla hata doner" }));

// POST /admin/config — Failure rate degistir
app.MapPost("/admin/config", (ConfigRequest req) =>
{
    failureRate = Math.Clamp(req.FailureRate, 0, 100);
    return Results.Ok(new { failureRate, description = $"%{failureRate} olasilikla hata doner" });
});

// GET /admin/log — Son N istegi gor
app.MapGet("/admin/log", (int? count) =>
{
    var items = requestLog.Reverse().Take(count ?? 20).ToList();
    return Results.Ok(new { total = requestLog.Count, items });
});

// DELETE /admin/log — Log'u temizle
app.MapDelete("/admin/log", () =>
{
    requestLog.Clear();
    flakyCounter.Clear();
    return Results.Ok(new { message = "Logs and flaky counters cleared." });
});

// GET /admin/stats — Basit istatistikler
app.MapGet("/admin/stats", () =>
{
    var all = requestLog.ToList();
    var total = all.Count;
    var success = all.Count(r => r.StatusCode is >= 200 and < 300);
    var failed = total - success;
    var byEndpoint = all.GroupBy(r => r.Endpoint)
        .Select(g => new { endpoint = g.Key, total = g.Count(), success = g.Count(r => r.StatusCode is >= 200 and < 300) })
        .ToList();

    return Results.Ok(new { total, success, failed, successRate = total > 0 ? $"%{success * 100 / total}" : "N/A", byEndpoint });
});

app.Run();

// ============================================================
// HELPERS
// ============================================================

static int PickFailureStatus()
{
    var failures = new[] { 500, 500, 500, 502, 503, 503, 408, 429 };
    return failures[Random.Shared.Next(failures.Length)];
}

static void LogRequest(ConcurrentQueue<RequestLogEntry> log, string endpoint, int statusCode, int delayMs, string body)
{
    log.Enqueue(new RequestLogEntry(endpoint, statusCode, delayMs, body.Length > 500 ? body[..500] : body, DateTime.UtcNow));

    // Keep max 1000 entries
    while (log.Count > 1000)
        log.TryDequeue(out _);
}

static string? ExtractField(string json, string field)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(field, out var val) ? val.ToString() : null;
    }
    catch { return null; }
}

record RequestLogEntry(string Endpoint, int StatusCode, int DelayMs, string Body, DateTime Timestamp);
record ConfigRequest(int FailureRate);
