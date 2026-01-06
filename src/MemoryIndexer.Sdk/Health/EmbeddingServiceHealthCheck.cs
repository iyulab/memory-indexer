using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for Embedding Service (Ollama / OpenAI / Local).
/// Monitors service availability and embedding generation performance.
/// </summary>
public class EmbeddingServiceHealthCheck : IHealthCheck
{
    private readonly IEmbeddingService _embeddingService;
    private const int CriticalEmbeddingLatencyMs = 2000;  // 2 seconds
    private const int WarningEmbeddingLatencyMs = 1000;   // 1 second

    public EmbeddingServiceHealthCheck(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTimeOffset.UtcNow;

            // Test embedding generation with a short text
            var testText = "health check test";
            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                testText,
                cancellationToken);

            var embeddingLatencyMs = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

            var data = new Dictionary<string, object>
            {
                ["embeddingLatencyMs"] = embeddingLatencyMs,
                ["embeddingDimensions"] = embedding.Length,
                ["serviceType"] = _embeddingService.GetType().Name,
                ["timestamp"] = DateTimeOffset.UtcNow
            };

            // Validate embedding quality
            if (embedding.Length == 0)
            {
                return HealthCheckResult.Unhealthy(
                    "Embedding service returned empty embedding",
                    data: data);
            }

            // Check for NaN or invalid values
            var embeddingArray = embedding.ToArray();
            if (embeddingArray.Any(v => float.IsNaN(v) || float.IsInfinity(v)))
            {
                return HealthCheckResult.Unhealthy(
                    "Embedding service returned invalid values (NaN/Infinity)",
                    data: data);
            }

            // Critical: Embedding latency too high
            if (embeddingLatencyMs > CriticalEmbeddingLatencyMs)
            {
                return HealthCheckResult.Unhealthy(
                    $"Embedding service has critical latency: {embeddingLatencyMs:F1}ms",
                    data: data);
            }

            // Degraded: Embedding latency elevated
            if (embeddingLatencyMs > WarningEmbeddingLatencyMs)
            {
                return HealthCheckResult.Degraded(
                    $"Embedding service has elevated latency: {embeddingLatencyMs:F1}ms",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"Embedding service healthy (latency: {embeddingLatencyMs:F1}ms, dims: {embedding.Length}, type: {data["serviceType"]})",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to connect to Embedding service",
                exception: ex);
        }
    }
}
