# Health Checks

Memory Indexer provides production-ready health check endpoints for monitoring system health and Kubernetes integration.

## Quick Start

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register health checks
builder.Services.AddMemoryIndexerHealthChecks();

var app = builder.Build();

// Map health endpoints
app.MapMemoryIndexerHealthChecks();

app.Run();
```

This configures:
- `/health` - Comprehensive health check (all components)
- `/health/live` - Liveness probe (is the app running?)
- `/health/ready` - Readiness probe (is the app ready for traffic?)
- `/health/startup` - Startup probe (has initialization completed?)

## Health Check Components

### Memory Tier Health Checks

| Check | Tags | Failure Status | Description |
|-------|------|----------------|-------------|
| Buffer | `tier`, `tier:sensory`, `memory` | Unhealthy | Monitors buffer processing lag and token accumulation |
| Short-Term Memory | `tier`, `tier:working`, `memory`, `critical` | Unhealthy | Checks capacity utilization (7±2 rule compliance) |
| Episodic Store | `tier`, `tier:episodic`, `memory`, `critical` | Unhealthy | Validates query latency for long-term storage |
| Semantic Store | `tier`, `tier:semantic`, `memory` | Degraded | Monitors entry count and confirmation ratios |

### Infrastructure Health Checks

| Check | Tags | Failure Status | Description |
|-------|------|----------------|-------------|
| Vector Database | `infrastructure`, `infrastructure:storage`, `critical` | Unhealthy | Tests vector search connectivity and latency |
| Embedding Service | `infrastructure`, `infrastructure:embedding`, `critical` | Unhealthy | Validates embedding generation capability |

## Kubernetes Integration

### Probe Configuration

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: memory-indexer
spec:
  template:
    spec:
      containers:
        - name: memory-indexer
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 3

          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 5
            timeoutSeconds: 3
            failureThreshold: 3

          startupProbe:
            httpGet:
              path: /health/startup
              port: 8080
            initialDelaySeconds: 0
            periodSeconds: 5
            timeoutSeconds: 3
            failureThreshold: 30
```

### Probe Semantics

**Liveness Probe (`/health/live`)**
- Returns `Healthy` if the process is running
- No actual health checks executed
- Use for: Detecting hung processes that need restart

**Readiness Probe (`/health/ready`)**
- Checks components tagged with `critical`
- Returns `Healthy` only when Vector DB and Embedding Service are available
- Use for: Traffic routing decisions

**Startup Probe (`/health/startup`)**
- Checks all memory tier components
- Ensures Buffer, Short-Term, Long-Term, and Archive stores are initialized
- Use for: Initial startup validation

## Response Format

Health check responses are JSON formatted:

```json
{
  "status": "Healthy",
  "totalDuration": "45ms",
  "entries": [
    {
      "name": "Buffer",
      "status": "Healthy",
      "description": "Sensory buffer healthy (5 items, 100 tokens)",
      "duration": "2ms",
      "tags": ["tier", "tier:sensory", "memory"],
      "data": {
        "itemCount": 5,
        "totalTokens": 100,
        "turnCount": 2,
        "processingLag": 0
      }
    },
    {
      "name": "Short-Term Memory",
      "status": "Healthy",
      "description": "Working memory healthy (3/7 = 42.9%)",
      "duration": "1ms",
      "tags": ["tier", "tier:working", "memory", "critical"],
      "data": {
        "count": 3,
        "capacity": 7,
        "utilizationRatio": 0.4285714285714286,
        "isFull": false
      }
    }
  ]
}
```

## Custom Endpoint Configuration

### Individual Endpoint Mapping

```csharp
// Map only specific endpoints
app.MapLivenessProbe("/healthz/live");
app.MapReadinessProbe("/healthz/ready");
app.MapStartupProbe("/healthz/startup");

// Map tier-specific health checks
app.MapMemoryTierHealthChecks("/health/memory");
app.MapInfrastructureHealthChecks("/health/infrastructure");
```

### Custom Path Prefix

```csharp
// Use custom path prefix
app.MapMemoryIndexerHealthChecks("/api/health");
// Creates: /api/health, /api/health/live, /api/health/ready, /api/health/startup
```

## Health Status Thresholds

### Buffer Health Check

| Metric | Warning | Critical |
|--------|---------|----------|
| Processing Lag | >60s | >120s |
| Token Accumulation | >2,000 | >5,000 |

### Short-Term Memory Health Check

| Metric | Warning | Critical |
|--------|---------|----------|
| Capacity Utilization | >85% | >95% or Full |

### Long-Term Store Health Check

| Metric | Warning | Critical |
|--------|---------|----------|
| Query Latency | >500ms | >1,000ms |

### Archive Store Health Check

| Metric | Warning | Critical |
|--------|---------|----------|
| Entry Count | >750 | >1,000 |
| Confirmation Ratio | <10% (with >50 entries) | - |

### Vector DB Health Check

| Metric | Warning | Critical |
|--------|---------|----------|
| Query Latency | >200ms | >500ms |

### Embedding Service Health Check

| Metric | Warning | Critical |
|--------|---------|----------|
| Generation Latency | >1,000ms | >2,000ms |
| Invalid Values | - | NaN or Infinity |

## Selective Health Check Registration

```csharp
// Register only memory tier checks
builder.Services.AddHealthChecks()
    .AddMemoryTierHealthChecks();

// Register only infrastructure checks
builder.Services.AddHealthChecks()
    .AddInfrastructureHealthChecks();

// Register specific checks
builder.Services.AddHealthChecks()
    .AddCheck<BufferHealthCheck>("Buffer", tags: new[] { "tier" })
    .AddCheck<VectorDbHealthCheck>("VectorDB", tags: new[] { "infrastructure" });
```

## Integration with Monitoring

### OpenTelemetry

Health check metrics are automatically exported when OpenTelemetry is configured:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHealthCheckPublisher());
```

### Prometheus

Health check results can be scraped via the `/metrics` endpoint:

```
# HELP health_check_status Health check status (1=healthy, 0=unhealthy)
# TYPE health_check_status gauge
health_check_status{name="Buffer"} 1
health_check_status{name="Short-Term Memory"} 1
health_check_status{name="Vector Database"} 1
```

## Troubleshooting

### Common Issues

**Buffer shows Unhealthy with high processing lag**
- Check if `BufferPromoter` service is running
- Verify Short-Term Memory has capacity for promotion
- Review buffer trigger configuration

**Short-Term Memory shows Unhealthy (full)**
- Working memory at capacity (7±2 rule)
- Check Long-Term Store connectivity for promotion
- Consider increasing capacity if appropriate

**Vector DB shows Unhealthy**
- Verify database connectivity (SQLite file access, Qdrant connection)
- Check query timeout settings
- Monitor disk space for SQLite

**Embedding Service shows Unhealthy**
- Verify Ollama/OpenAI service availability
- Check model loading status
- Review API key configuration

## Best Practices

1. **Use appropriate probes**: Liveness for process health, readiness for traffic, startup for initialization
2. **Set reasonable timeouts**: Match probe timeouts to expected response times
3. **Monitor degraded states**: Degraded status indicates potential issues before failure
4. **Log health check data**: Use the data dictionary for debugging and trending
5. **Separate concerns**: Use tag-based filtering for targeted monitoring

## See Also

- [Architecture](ARCHITECTURE.md) - 4-tier memory architecture
- [Configuration](GUIDES.md) - Health check configuration options
- [Observability](ARCHITECTURE.md#observability) - OpenTelemetry integration
