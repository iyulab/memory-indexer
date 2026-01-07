# Best Practices

Production-ready guidelines for deploying and operating Memory Indexer.

## Table of Contents

- [Configuration](#configuration)
- [Memory Management](#memory-management)
- [Performance Optimization](#performance-optimization)
- [Security](#security)
- [Monitoring & Observability](#monitoring--observability)
- [Testing](#testing)
- [Common Pitfalls](#common-pitfalls)

---

## Configuration

### Environment-Specific Configuration

**Development**:
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "InMemory",
      "ConnectionString": null
    },
    "Embedding": {
      "Provider": "Mock",
      "Dimensions": 384
    },
    "VCM": {
      "WorkingMemory": { "Capacity": 3 }
    }
  }
}
```

**Staging**:
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "Data Source=staging-memory.db"
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "bge-m3",
      "BaseUrl": "http://ollama:11434"
    },
    "VCM": {
      "WorkingMemory": {
        "Capacity": 7,
        "LazyEmbeddingLoading": true
      }
    }
  }
}
```

**Production**:
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "Qdrant",
      "ConnectionString": "https://qdrant-prod.example.com:6334",
      "ApiKey": "#{QDRANT_API_KEY}#"
    },
    "Embedding": {
      "Provider": "OpenAI",
      "Model": "text-embedding-3-large",
      "ApiKey": "#{OPENAI_API_KEY}#",
      "Dimensions": 1024
    },
    "VCM": {
      "WorkingMemory": {
        "Capacity": 7,
        "DefaultTtl": "00:10:00",
        "LazyEmbeddingLoading": true
      },
      "RecentlyBuffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "UserProfile": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8
      }
    }
  }
}
```

### Secrets Management

**❌ Don't**: Commit API keys to source control

```json
{
  "Embedding": {
    "ApiKey": "sk-abc123..."  // NEVER DO THIS
  }
}
```

**✅ Do**: Use environment variables or secret management

```csharp
builder.Services.AddMemoryIndexer(options =>
{
    options.Embedding.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set");
});
```

**✅ Do**: Use Azure Key Vault, AWS Secrets Manager, or Kubernetes Secrets

```csharp
// Azure Key Vault
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
var credential = new DefaultAzureCredential();
builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl!), credential);

// Secrets automatically loaded from Key Vault
builder.Services.AddMemoryIndexer(options =>
{
    options.Embedding.ApiKey = builder.Configuration["OpenAI-ApiKey"];
});
```

---

## Memory Management

### Working Memory Capacity Tuning

**Guideline**: Follow Baddeley's Working Memory Model (4-7 chunks)

```csharp
// Low-context applications (simple Q&A)
options.VCM.WorkingMemory.Capacity = 4;

// Medium-context (conversational agents)
options.VCM.WorkingMemory.Capacity = 7;  // Recommended default

// High-context (complex multi-turn tasks)
options.VCM.WorkingMemory.Capacity = 10; // Use with caution
```

**Warning**: Capacity > 10 may degrade performance and increase memory pressure.

### Enable Lazy Embedding Loading

**For memory-constrained environments** (containers with <2GB RAM):

```csharp
options.VCM.WorkingMemory.LazyEmbeddingLoading = true;
```

**Benefits**:
- ~3KB memory savings per memory unit
- Reduces Working Memory footprint by 40-60%
- Embeddings restored on demotion to Session tier

**Trade-offs**:
- Slight performance overhead on demotion (embedding restoration)
- Not recommended for high-throughput scenarios

### Memory Pressure Monitoring

**Production configuration**:

```csharp
services.AddSingleton<IMemoryPressureMonitor, MemoryPressureMonitorService>();

// Register callback for critical pressure
var monitor = serviceProvider.GetRequiredService<IMemoryPressureMonitor>();
monitor.OnPressureChanged(level =>
{
    if (level == MemoryPressureLevel.Critical)
    {
        _logger.LogWarning("Critical memory pressure detected");
        // Trigger emergency eviction
    }
});
```

**Alerting thresholds**:
- **Medium (60-80%)**: Log warning, consider scale-up
- **High (80-90%)**: Alert on-call, trigger proactive eviction
- **Critical (>90%)**: Page immediately, emergency capacity reduction

---

## Performance Optimization

### Embedding Provider Selection

| Provider | Latency | Quality | Cost | Use Case |
|----------|---------|---------|------|----------|
| **Local (ONNX)** | ~10ms | Good | Free | Offline, low-latency, cost-sensitive |
| **Ollama** | ~50ms | Excellent | Free | On-premise, privacy-sensitive |
| **OpenAI** | ~100ms | Best | Paid | Production, highest quality needed |

**Recommendation**:
- **Development**: Local or Mock
- **Staging**: Ollama
- **Production**: OpenAI (with caching)

### Batch Operations

**❌ Avoid**: Sequential operations

```csharp
foreach (var message in messages)
{
    await _memory.EncodeAsync(new MemoryUnit { Content = message });
}
// Total time: N × embedding_latency
```

**✅ Prefer**: Batch encoding

```csharp
var memories = messages.Select(m => new MemoryUnit { Content = m });
await _memory.EncodeBatchAsync(memories);
// Total time: ~embedding_latency (parallelized)
```

### Embedding Caching

**For read-heavy workloads**:

```csharp
public class CachedEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _inner;
    private readonly IMemoryCache _cache;

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"emb:{text.GetHashCode()}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            entry.Size = 1024; // Embedding dimension

            return await _inner.GenerateEmbeddingAsync(text, cancellationToken);
        });
    }
}

// Register
services.Decorate<IEmbeddingService, CachedEmbeddingService>();
```

**Benefits**: 95%+ cache hit rate for conversational AI (repeated phrases)

### Vector Search Optimization

**Qdrant configuration for production**:

```yaml
storage:
  # Use HNSW for fast approximate search
  hnsw_config:
    m: 16              # Number of edges per node
    ef_construct: 100  # Construction-time accuracy
    full_scan_threshold: 10000

  # Enable on-disk storage for large datasets
  on_disk: true

  # Quantization for memory efficiency
  quantization_config:
    scalar:
      type: int8
      quantile: 0.99
      always_ram: true
```

**Performance targets**:
- **Latency**: p95 < 100ms for 10K vectors
- **Throughput**: >100 QPS per replica
- **Accuracy**: >95% recall@10

---

## Security

### Input Validation

**Always validate user input**:

```csharp
public class MemoryValidationService
{
    private const int MaxContentLength = 10_000;
    private const int MaxMetadataSize = 100;

    public ValidationResult ValidateMemoryUnit(MemoryUnit memory)
    {
        if (string.IsNullOrWhiteSpace(memory.Content))
            return ValidationResult.Error("Content cannot be empty");

        if (memory.Content.Length > MaxContentLength)
            return ValidationResult.Error($"Content exceeds {MaxContentLength} chars");

        if (memory.Metadata?.Count > MaxMetadataSize)
            return ValidationResult.Error($"Metadata exceeds {MaxMetadataSize} entries");

        // Sanitize HTML/script injection
        memory.Content = SanitizeContent(memory.Content);

        return ValidationResult.Success();
    }

    private string SanitizeContent(string content)
    {
        // Remove potentially dangerous content
        return Regex.Replace(content, @"<script[^>]*>.*?</script>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }
}
```

### User Isolation

**Ensure strict user boundary enforcement**:

```csharp
// Good: Always filter by userId
var memories = await _memory.RetrieveAsync(userId, query);

// Bad: Global search without userId filter
var memories = await _memory.RetrieveAsync(null, query); // ❌ NEVER
```

**Implement multi-tenancy safeguards**:

```csharp
public class TenantIsolationMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst("user_id")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            context.Response.StatusCode = 401;
            return;
        }

        // Store userId in scoped service for automatic filtering
        var tenantContext = context.RequestServices
            .GetRequiredService<ITenantContext>();

        tenantContext.SetUserId(userId);

        await _next(context);
    }
}
```

### Data Privacy

**PII handling**:

```csharp
public class PiiRedactionService
{
    public string RedactPii(string content)
    {
        // Email
        content = Regex.Replace(content,
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            "[EMAIL]");

        // Phone numbers
        content = Regex.Replace(content,
            @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b",
            "[PHONE]");

        // Credit cards
        content = Regex.Replace(content,
            @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b",
            "[CARD]");

        return content;
    }
}
```

**GDPR compliance** (Right to be Forgotten):

```csharp
public async Task DeleteUserDataAsync(string userId)
{
    // Soft delete all memories
    var memories = await _memory.ListAsync(userId);

    foreach (var memory in memories)
    {
        await _memory.DeleteAsync(memory.Id);
    }

    // Hard delete from vector store (if required by regulation)
    await _store.PurgeUserAsync(userId);

    _logger.LogInformation("User {UserId} data deleted", userId);
}
```

---

## Monitoring & Observability

### Health Checks

**Comprehensive health check configuration**:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<MemoryIndexerHealthCheck>("memory-indexer")
    .AddCheck<QdrantHealthCheck>("qdrant")
    .AddCheck<EmbeddingProviderHealthCheck>("embedding-provider");

app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

### OpenTelemetry Integration

**Full observability stack**:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("MemoryIndexer.*")
            .AddPrometheusExporter();
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("MemoryIndexer.*")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddJaegerExporter();
    });
```

**Key metrics to monitor**:

```csharp
public class MemoryMetrics
{
    private readonly Counter<long> _memoryStored;
    private readonly Histogram<double> _retrievalLatency;
    private readonly ObservableGauge<int> _workingMemorySize;

    public MemoryMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("MemoryIndexer.Metrics");

        _memoryStored = meter.CreateCounter<long>(
            "memory.stored.count",
            description: "Number of memories stored");

        _retrievalLatency = meter.CreateHistogram<double>(
            "memory.retrieval.latency",
            unit: "ms",
            description: "Memory retrieval latency");

        _workingMemorySize = meter.CreateObservableGauge<int>(
            "memory.working.size",
            () => GetCurrentWorkingMemorySize(),
            description: "Current Working Memory size");
    }
}
```

**Alerts**:
- `memory.retrieval.latency` p95 > 100ms
- `memory.working.size` > 80% of capacity
- `memory.pressure.level` >= High
- `health.check.status` == Unhealthy

---

## Testing

### Unit Testing

**Test memory operations**:

```csharp
[Fact]
public async Task EncodeAsync_ShouldStoreMemoryWithEmbedding()
{
    // Arrange
    var options = Options.Create(new MemoryIndexerOptions
    {
        Storage = { Type = StorageType.InMemory },
        Embedding = { Provider = EmbeddingProvider.Mock }
    });

    var services = new ServiceCollection();
    services.AddMemoryIndexer(options);
    var provider = services.BuildServiceProvider();

    var memory = provider.GetRequiredService<IMemoryPrimitives>();

    // Act
    var unit = new MemoryUnit
    {
        UserId = "test-user",
        Content = "Test content"
    };

    await memory.EncodeAsync(unit);

    // Assert
    var retrieved = await memory.RetrieveAsync("test-user", "Test", limit: 1);
    Assert.Single(retrieved);
    Assert.Equal("Test content", retrieved.First().Content);
    Assert.NotNull(retrieved.First().Embedding);
}
```

### Integration Testing

**Test with real dependencies**:

```csharp
public class MemoryIndexerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task EndToEnd_StoreAndRecall_ShouldWork()
    {
        // Arrange
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddMemoryIndexer(options =>
                    {
                        options.Storage.Type = StorageType.SqliteVec;
                        options.Storage.ConnectionString = "Data Source=:memory:";
                        options.Embedding.Provider = EmbeddingProvider.Local;
                    });
                });
            });

        var client = factory.CreateClient();

        // Act: Store memory
        var storeResponse = await client.PostAsJsonAsync("/api/memory", new
        {
            userId = "test-user",
            content = "I prefer TypeScript"
        });

        storeResponse.EnsureSuccessStatusCode();

        // Act: Recall memory
        var recallResponse = await client.GetAsync(
            "/api/memory?userId=test-user&query=TypeScript");

        var memories = await recallResponse.Content
            .ReadFromJsonAsync<List<MemoryUnit>>();

        // Assert
        Assert.NotEmpty(memories);
        Assert.Contains("TypeScript", memories.First().Content);
    }
}
```

### Load Testing

**Use k6 for performance testing**:

```javascript
// load-test.js
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  stages: [
    { duration: '2m', target: 50 },   // Ramp up
    { duration: '5m', target: 100 },  // Sustained load
    { duration: '2m', target: 0 },    // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<100'], // 95% < 100ms
  },
};

export default function () {
  const payload = JSON.stringify({
    userId: `user-${__VU}`,
    content: `Test message ${__ITER}`,
  });

  const params = {
    headers: { 'Content-Type': 'application/json' },
  };

  let response = http.post('http://localhost:5000/api/memory', payload, params);

  check(response, {
    'status is 200': (r) => r.status === 200,
    'latency < 100ms': (r) => r.timings.duration < 100,
  });

  sleep(1);
}
```

Run: `k6 run load-test.js`

---

## Common Pitfalls

### 1. Not Using VCM

**❌ Antipattern**: Direct storage bypass

```csharp
// Bypasses 4-tier architecture
await _memoryStore.StoreAsync(memory);
```

**✅ Solution**: Always use VCM

```csharp
// Respects tier promotion logic
await _vcm.AddToRecentlyAsync(userId, content);
```

### 2. Over-Eager Promotion

**❌ Antipattern**: Promoting everything to User Profile

```csharp
// Promotes noise to long-term storage
await _profile.StoreFactAsync(new UserFact
{
    Confidence = 0.5, // Too low
    ObservationCount = 1 // Not confirmed
});
```

**✅ Solution**: Respect confirmation thresholds

```csharp
// Only promote high-confidence facts
if (fact.Confidence >= 0.8 && fact.ObservationCount >= 3)
{
    await _profile.StoreFactAsync(fact);
}
```

### 3. Ignoring Memory Pressure

**❌ Antipattern**: Unbounded memory growth

```csharp
// No capacity limits or eviction
while (true)
{
    await _workingMemory.PromoteAsync(memory);
}
```

**✅ Solution**: Monitor and adapt

```csharp
var pressure = _pressureMonitor.CurrentPressure;

if (pressure >= MemoryPressureLevel.High)
{
    await _workingMemory.EvictLeastRelevantAsync(count: 3);
}
```

### 4. Synchronous Blocking

**❌ Antipattern**: Blocking async operations

```csharp
// Blocks thread pool
var memory = _memory.EncodeAsync(unit).Result;
```

**✅ Solution**: Proper async/await

```csharp
var memory = await _memory.EncodeAsync(unit);
```

### 5. Missing Error Handling

**❌ Antipattern**: No exception handling

```csharp
var memories = await _memory.RetrieveAsync(userId, query);
```

**✅ Solution**: Graceful degradation

```csharp
try
{
    var memories = await _memory.RetrieveAsync(userId, query);
}
catch (VectorStoreException ex)
{
    _logger.LogError(ex, "Vector store unavailable");
    return Array.Empty<MemoryUnit>(); // Fallback
}
```

---

## Checklist for Production Deployment

- [ ] **Configuration**
  - [ ] Secrets stored in secure vault (not in code)
  - [ ] Environment-specific configs for dev/staging/prod
  - [ ] Connection strings validated

- [ ] **Memory Management**
  - [ ] Working Memory capacity tuned (4-7 recommended)
  - [ ] Lazy embedding loading enabled for <2GB RAM
  - [ ] Memory pressure monitoring configured

- [ ] **Performance**
  - [ ] Embedding provider selected (OpenAI for production)
  - [ ] Batch operations used for bulk stores
  - [ ] Vector search optimized (HNSW + quantization)

- [ ] **Security**
  - [ ] Input validation enabled
  - [ ] User isolation enforced
  - [ ] PII redaction implemented
  - [ ] GDPR compliance (right to be forgotten)

- [ ] **Observability**
  - [ ] Health checks configured (/health/startup, /ready, /live)
  - [ ] OpenTelemetry metrics exported
  - [ ] Alerts configured for SLOs
  - [ ] Logging aggregation set up

- [ ] **Testing**
  - [ ] Unit tests > 80% coverage
  - [ ] Integration tests for critical paths
  - [ ] Load tests validate SLOs (p95 < 100ms)

- [ ] **Deployment**
  - [ ] Kubernetes manifests reviewed
  - [ ] HPA configured (2-10 replicas)
  - [ ] Resource limits set (256Mi-1Gi)
  - [ ] Backup strategy defined

---

## Next Steps

- **Quick Start**: [Quick Start Guide](QUICKSTART.md)
- **Common Patterns**: [Patterns](PATTERNS.md)
- **Integrations**: [LLM Framework Integrations](INTEGRATIONS.md)
- **Deployment**: [Kubernetes Guide](../deploy/kubernetes/README.md)
