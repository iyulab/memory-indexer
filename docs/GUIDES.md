# Usage Guides & Best Practices

Practical patterns and best practices for Memory Indexer in production applications.

## Table of Contents

- [Configuration Guide](#configuration-guide)
- [Custom IMemoryStore Implementation](#custom-imemorystore-implementation)
- [Common Usage Patterns](#common-usage-patterns)
- [Best Practices](#best-practices)
- [Anti-Patterns](#anti-patterns)
- [Performance Optimization](#performance-optimization)
- [Production Checklist](#production-checklist)

---

## Configuration Guide

### Full Configuration Schema

Memory Indexer uses `MemoryIndexerOptions` for configuration. Below is the complete schema with default values:

```json
{
  "MemoryIndexer": {
    "DefaultUserId": "default",
    "Storage": {
      "Type": "InMemory",
      "ConnectionString": "memories.db"
    },
    "Embedding": {
      "Provider": "Mock",
      "Model": "bge-m3",
      "Endpoint": "http://localhost:11434",
      "ApiKey": null,
      "Dimensions": 1024
    },
    "Completion": {
      "Provider": "Mock",
      "Model": "llama3.2",
      "Endpoint": "http://localhost:11434",
      "ApiKey": null
    },
    "VCM": {
      "Buffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "ShortTermMemory": {
        "Capacity": 9,
        "DefaultTtl": "00:10:00"
      },
      "LongTermStore": {
        "MaxSessionMemories": 1000
      },
      "ArchiveStore": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8
      }
    },
    "Intelligence": {
      "EnableDeduplication": true,
      "DeduplicationThreshold": 0.95,
      "EnableConflictDetection": true,
      "EnableEntityExtraction": true
    },
    "Latency": {
      "EmbeddingCacheEnabled": true,
      "EmbeddingCacheSize": 1000,
      "EmbeddingCacheTtlMinutes": 60
    }
  }
}
```

### Configuration Options Reference

| Section | Option | Type | Default | Description |
|---------|--------|------|---------|-------------|
| *(root)* | **DefaultUserId** | string | `"default"` | Fallback user ID used by MCP tools and REST controllers when no explicit user ID is provided in a request. Override to isolate single-user deployments or set a meaningful default identity. |
| **Storage** | Type | string | "InMemory" | Storage provider: `InMemory`, `SqliteVec` |
| | ConnectionString | string | "memories.db" | Database path for SqliteVec |
| **Embedding** | Provider | string | "Mock" | `Mock`, `Ollama`, `Custom` (inject your own IEmbeddingService for `Custom`) |
| | Dimensions | int | 1024 | Vector dimensions (must match your embedding model) |
| **VCM.Buffer** | MaxIdleSeconds | int | 60 | Promote to Short after idle timeout |
| | TokenThreshold | int | 500 | Promote when buffer exceeds token count |
| | TurnThreshold | int | 3 | Promote after N turns |
| **VCM.ShortTermMemory** | Capacity | int | 9 | Working memory capacity (7±2 rule) |
| | DefaultTtl | TimeSpan | 00:10:00 | Time-to-live before promotion to Long |
| **VCM.ArchiveStore** | MinConfirmationCount | int | 3 | Required confirmations for Archive promotion |
| | MinConfidenceThreshold | float | 0.8 | Required confidence for Archive promotion |

### DI Registration Patterns

**Basic (InMemory + Mock Embedding):**
```csharp
services.AddMemoryIndexer();  // Uses defaults
```

**With External Embedding Service:**
```csharp
// 1. Register your embedding service FIRST
services.AddSingleton<IEmbeddingService, MyOpenAIEmbeddingService>();

// 2. Then add Memory Indexer
services.AddMemoryIndexer(options =>
{
    options.Embedding.Dimensions = 1536;  // Match your model
});
```

**With SQLite Persistent Storage:**
```csharp
services.AddMemoryIndexer(options =>
{
    options.Storage.ConnectionString = "myapp_memories.db";
    options.Embedding.Dimensions = 1024;
}).WithSqliteVec();
```

**With Custom IMemoryStore:**
```csharp
// 1. Register your custom store
services.AddSingleton<IMemoryStore, MyPostgresMemoryStore>();

// 2. Add Memory Indexer (it will use your registered store)
services.AddMemoryIndexer(options =>
{
    options.Embedding.Dimensions = 1536;
});
```

### DefaultUserId

`DefaultUserId` sets the fallback user identity used by MCP tools and REST controllers when a request does not include an explicit user ID. It is a top-level option on `MemoryIndexerOptions` (default: `"default"`).

Configure it via the options delegate or via `appsettings.json`:

```csharp
services.AddMemoryIndexer(options =>
{
    options.DefaultUserId = "assistant";  // Override the fallback identity
});
```

```json
{
  "MemoryIndexer": {
    "DefaultUserId": "assistant"
  }
}
```

This setting was previously a private hardcoded constant inside individual MCP tool classes. It is now centralized in `MemoryIndexerOptions` so all SDK consumers share a single configurable default. Consumers who relied on the implicit `"default"` value do not need to change anything — the default is preserved.

---

### Configuration Validation

Memory Indexer includes built-in configuration validation (21 rules). Validation runs automatically at startup and logs warnings for common issues:

```csharp
// Enable detailed validation logging
services.AddMemoryIndexer(options =>
{
    // ...
}).ValidateOnStart();  // Throws if critical config issues found
```

---

## Custom IMemoryStore Implementation

Memory Indexer provides `IMemoryStore` interface for custom storage backends. This enables integration with any database or vector store (PostgreSQL, Qdrant, Redis, Pinecone, etc.).

### Interface Overview

```csharp
public interface IMemoryStore
{
    // Core CRUD
    Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(IEnumerable<MemoryUnit> memories, CancellationToken ct = default);
    Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, bool hardDelete = false, CancellationToken ct = default);

    // Bulk operations
    Task<int> DeleteByUserAsync(string userId, bool hardDelete = false, CancellationToken ct = default);
    Task<int> DeleteBySessionAsync(string userId, string sessionId, bool hardDelete = false, CancellationToken ct = default);

    // Search & retrieval
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, MemorySearchOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryUnit>> GetAllAsync(string userId, MemoryFilterOptions? options = null, CancellationToken ct = default);

    // Statistics
    Task<long> GetCountAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<MemoryType, int>> GetTypeCountsAsync(string userId, CancellationToken ct = default);

    // Lifecycle
    Task EnsureCollectionExistsAsync(CancellationToken ct = default);
    Task DeleteCollectionAsync(CancellationToken ct = default);
}
```

### Helper Extensions

Use `MemoryStoreExtensions` to reduce boilerplate:

```csharp
using MemoryIndexer.Utilities;

public class MyPostgresMemoryStore : IMemoryStore
{
    private readonly MyDbContext _db;

    public async Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken ct)
    {
        // Use extension methods for common logic
        memory.PrepareForStore();   // Sets Id, CreatedAt, UpdatedAt
        memory.ValidateForStore();  // Throws if invalid

        // Your PostgreSQL storage logic
        var entity = MapToEntity(memory);
        await _db.Memories.AddAsync(entity, ct);
        await _db.SaveChangesAsync(ct);

        return memory;
    }

    public Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(
        IEnumerable<MemoryUnit> memories, CancellationToken ct)
    {
        // Option 1: Use default implementation (iterates StoreAsync)
        return this.StoreBatchDefaultAsync(memories, ct);

        // Option 2: Implement native batch for better performance
        // using PostgreSQL COPY or bulk insert
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        MemorySearchOptions options,
        CancellationToken ct)
    {
        options.ValidateSearchOptions();

        // Get candidates from your vector index (e.g., pgvector)
        var candidates = await GetCandidatesFromPgVector(queryEmbedding, options, ct);

        // Use extension for similarity calculation if needed
        return candidates
            .ApplySearchFilter(options)
            .CalculateSimilarityResults(queryEmbedding, options.MinScore, options.Limit);
    }

    public Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct)
    {
        // Option 1: Default implementation
        return this.GetByIdsDefaultAsync(ids, ct);

        // Option 2: Native multi-get (recommended)
        // return _db.Memories.Where(m => ids.Contains(m.Id)).ToListAsync(ct);
    }
}
```

### Available Extension Methods

| Method | Purpose |
|--------|---------|
| `PrepareForStore()` | Sets Id (if empty), CreatedAt, UpdatedAt |
| `ValidateForStore()` | Throws if UserId or Content is missing |
| `ValidateSearchOptions()` | Validates search parameters |
| `StoreBatchDefaultAsync()` | Default batch store (iterates StoreAsync) |
| `GetByIdsDefaultAsync()` | Default multi-get (iterates GetByIdAsync) |
| `ApplyFilter()` | LINQ filter for MemoryFilterOptions |
| `ApplySearchFilter()` | LINQ filter for MemorySearchOptions |
| `CalculateSimilarityResults()` | Cosine similarity calculation |
| `HasDuplicateHash()` | Check for content hash duplicates |

### Hybrid Storage Pattern (PostgreSQL + Qdrant)

For production scenarios requiring both relational queries and vector search:

```csharp
public class HybridMemoryStore : IMemoryStore
{
    private readonly IDbContextFactory<MemoryDbContext> _dbFactory;
    private readonly QdrantClient _qdrant;

    public async Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken ct)
    {
        memory.PrepareForStore();
        memory.ValidateForStore();

        // 1. Store metadata in PostgreSQL
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Memories.Add(MapToEntity(memory));
        await db.SaveChangesAsync(ct);

        // 2. Store vector in Qdrant
        if (memory.Embedding.HasValue)
        {
            await _qdrant.UpsertAsync(
                collectionName: "memories",
                points: new[] { MapToPoint(memory) },
                cancellationToken: ct);
        }

        return memory;
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        MemorySearchOptions options,
        CancellationToken ct)
    {
        // 1. Vector search in Qdrant
        var searchResult = await _qdrant.SearchAsync(
            collectionName: "memories",
            vector: queryEmbedding.ToArray(),
            filter: BuildQdrantFilter(options),
            limit: (ulong)options.Limit,
            cancellationToken: ct);

        // 2. Fetch full metadata from PostgreSQL
        var ids = searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var memories = await db.Memories
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(ct);

        // 3. Combine results with scores
        return searchResult
            .Select(r => new MemorySearchResult
            {
                Memory = memories.First(m => m.Id == Guid.Parse(r.Id.Uuid)),
                Score = r.Score
            })
            .ToList();
    }
}
```

### Type Naming Considerations

When implementing `IMemoryStore`, you may encounter type name conflicts between your own `MemoryUnit` DTO and `MemoryIndexer.Models.MemoryUnit`. Solutions:

```csharp
// Option 1: Use fully qualified name
public MemoryIndexer.Models.MemoryUnit MapFromEntity(MyMemoryEntity entity)
{
    return new MemoryIndexer.Models.MemoryUnit { ... };
}

// Option 2: Use type alias
using MiMemoryUnit = MemoryIndexer.Models.MemoryUnit;

public MiMemoryUnit MapFromEntity(MyMemoryEntity entity)
{
    return new MiMemoryUnit { ... };
}

// Option 3: Rename your type (recommended for new projects)
public class MemoryEntity { }  // Your EF entity
public class MemoryDto { }     // Your API DTO
```

---

## Common Usage Patterns

### 1. Conversation History Management

**Pattern**: Automatic conversation archiving with 4-tier lifecycle

```csharp
public class ConversationService
{
    private readonly IVirtualContextManager _vcm;

    public async Task ProcessUserMessage(string userId, string message)
    {
        // Store in Recently Buffer (Tier 0)
        await _vcm.AddToRecentlyAsync(userId, message, metadata: new()
        {
            ["role"] = "user",
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        });

        // Automatic promotion:
        // Recently → Working (60s OR 500 tokens OR 3 turns)
        // Working → Session (topic change OR 10min)
        // Session → User (confidence ≥ 0.8 AND confirms ≥ 3)
    }

    public async Task<string> GetConversationContext(string userId)
    {
        var context = await _vcm.RetrieveHybridAsync(userId, limit: 10);
        return string.Join("\n\n", context.Select(m =>
            $"[{m.Metadata["role"]}]: {m.Content}"));
    }
}
```

### 2. User Preference Learning

**Pattern**: Gradual confidence building with confirmation tracking

```csharp
public class PreferenceService
{
    private readonly MemoryService _memory;

    public async Task LearnPreference(string userId, string preference)
    {
        await _memory.StoreAsync(
            userId: userId,
            content: preference,
            type: MemoryType.Fact,
            importance: 0.7f,
            metadata: new() { ["category"] = "preference" }
        );

        // Automatically promotes to User Profile when:
        // - Mentioned 3+ times AND
        // - Confidence ≥ 0.8
    }

    public async Task<List<string>> GetUserPreferences(string userId)
    {
        var prefs = await _memory.RecallAsync(
            userId: userId,
            query: "user preferences",
            metadataFilter: new() { ["category"] = "preference" },
            limit: 10
        );

        return prefs.Select(r => r.Memory.Content).ToList();
    }
}
```

### 3. Entity Relationship Tracking

**Pattern**: Graph-based entity management

```csharp
public class RelationshipTracker
{
    private readonly IMemoryGraphService _graph;

    public async Task TrackRelationship(
        string userId,
        string entity1,
        string entity2,
        string relationship)
    {
        await _graph.AddRelationshipAsync(
            userId: userId,
            sourceEntity: entity1,
            targetEntity: entity2,
            relationshipType: relationship,
            confidence: 0.9f
        );
    }

    public async Task<List<string>> FindRelatedEntities(
        string userId,
        string entity)
    {
        var related = await _graph.FindRelatedMemoriesAsync(
            userId: userId,
            startEntity: entity,
            maxHops: 2,
            minConfidence: 0.5f
        );

        return related.Select(m => m.Content).ToList();
    }
}
```

### 4. Session Continuity

**Pattern**: Session-scoped memory with automatic summarization

```csharp
public class SessionManager
{
    private readonly MemoryService _memory;

    public async Task StartSession(string userId, string sessionId)
    {
        await _memory.StoreAsync(
            userId: userId,
            content: $"Session started: {sessionId}",
            type: MemoryType.Episodic,
            sessionId: sessionId,
            importance: 0.3f
        );
    }

    public async Task<string> GetSessionSummary(string userId, string sessionId)
    {
        var memories = await _memory.GetAllAsync(
            userId: userId,
            options: new MemoryFilterOptions
            {
                SessionId = sessionId,
                OrderBy = MemoryOrderBy.CreatedAtDesc
            }
        );

        // Summarization happens automatically during consolidation
        var summaries = memories
            .Where(m => m.Type == MemoryType.Semantic)
            .Select(m => m.Content);

        return string.Join("\n", summaries);
    }
}
```

### 5. Context Budget API (Recommended)

**Pattern**: Token-budget-aware context building for LLM calls

```csharp
public class ChatService
{
    private readonly IContextBuilder _contextBuilder;

    public async Task<string> GenerateResponse(
        string userId, string sessionId, string userMessage)
    {
        // Build context with token budget
        var request = new ContextRequest(
            UserId: userId,
            SessionId: sessionId,
            Query: userMessage,
            Budget: new ContextBudget(TotalTokens: 2000)
        );

        // Use strategy based on use case:
        // - "RecentHeavy": Games, multi-turn conversations
        // - "Balanced": General chat
        // - "SemanticHeavy": RAG, Q&A systems
        var bundle = await _contextBuilder.BuildAsync(request, "RecentHeavy");

        // Send to LLM - NO conversation history needed!
        var response = await _llm.GenerateAsync(new
        {
            system = $"Context from memory:\n{bundle.Content}",
            user = userMessage
        });

        return response;
    }
}
```

**Benefits:**
- O(1) token cost regardless of conversation length
- Session-isolated episodic memories
- Cross-session user facts automatically included
- Configurable allocation (recent vs semantic)

### 6. Memory Reflection & Insights

**Pattern**: Periodic reflection for pattern discovery

```csharp
public class ReflectionService
{
    private readonly IReflectionEngine _reflection;

    public async Task PerformDailyReflection(string userId)
    {
        // Check if reflection is needed (importance threshold)
        var shouldReflect = await _reflection.ShouldReflectAsync(
            userId: userId,
            importanceThreshold: 0.7f
        );

        if (shouldReflect)
        {
            var insights = await _reflection.ReflectAsync(
                userId: userId,
                lookbackDays: 7,
                maxInsights: 5
            );

            foreach (var insight in insights)
            {
                // Insights are automatically stored as Semantic memories
                Console.WriteLine($"Insight: {insight.Content}");
            }
        }
    }
}
```

---

## Best Practices

### Environment-Specific Configuration

**Development:**
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "InMemory"  // Fast, no persistence
    },
    "Embedding": {
      "Provider": "Mock",  // No external dependencies
      "Dimensions": 384
    }
  }
}
```

**Staging:**
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "staging_memory.db"
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "bge-m3",
      "Endpoint": "http://ollama-staging:11434"
    }
  }
}
```

**Production:**
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "Qdrant",
      "Endpoint": "https://qdrant.production:6333",
      "ApiKey": "${QDRANT_API_KEY}"  // From environment
    },
    "Embedding": {
      "Provider": "Custom",
      "Model": "text-embedding-3-large",
      "ApiKey": "${OPENAI_API_KEY}"
    },
    "VCM": {
      "WorkingMemory": { "Capacity": 10 },  // Larger for production
      "EnableAutoMaintenance": true
    }
  }
}
```

### Secrets Management

**❌ Wrong:**
```csharp
options.Embedding.ApiKey = "sk-1234567890abcdef";  // Hardcoded
```

**✅ Correct:**
```csharp
// Use environment variables
options.Embedding.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

// Or use .NET Secret Manager (development)
builder.Configuration.AddUserSecrets<Program>();

// Or use Azure Key Vault (production)
builder.Configuration.AddAzureKeyVault(
    new Uri("https://keyvault.vault.azure.net/"),
    new DefaultAzureCredential()
);
```

### Memory Type Selection

| Use Case | Memory Type | Reason |
|----------|-------------|--------|
| User said "I like apples" | `Episodic` | Event in conversation |
| Extracted fact: "User likes apples" | `Fact` | Confirmed assertion |
| "How to reset password" | `Procedural` | Step-by-step knowledge |
| "Apples are fruits" | `Semantic` | General knowledge |

### Importance Scoring Guidelines

| Importance | Use Case | Example |
|------------|----------|---------|
| 0.9-1.0 | Critical user data | "User is allergic to peanuts" |
| 0.7-0.8 | Important preferences | "User prefers dark mode" |
| 0.5-0.6 | General facts | "User mentioned liking coffee" |
| 0.3-0.4 | Contextual info | "User asked about weather" |
| 0.1-0.2 | Ephemeral data | "User said 'hmm'" |

### Metadata Best Practices

**✅ Use metadata for:**
- Filtering (`ContentType`, `Category`, `Source`)
- Time-series data (`Round`, `Turn`, `Step`)
- Structured categorization (`Priority`, `Status`)

**❌ Don't use metadata for:**
- Large text blobs (use `Content` field)
- Deeply nested objects (flatten first)
- Data that needs semantic search (use `Content`)

---

## Anti-Patterns

### ❌ Anti-Pattern 1: Storing Everything

**Problem:**
```csharp
// Storing every minor detail
await memory.StoreAsync(userId, "User typed 'a'", importance: 0.5f);
await memory.StoreAsync(userId, "User backspaced", importance: 0.5f);
```

**Solution:**
```csharp
// Store only meaningful interactions
await memory.StoreAsync(
    userId,
    "User searched for 'machine learning tutorials'",
    importance: 0.6f
);
```

### ❌ Anti-Pattern 2: Ignoring Memory Types

**Problem:**
```csharp
// Everything as Episodic
await memory.StoreAsync(userId, content, type: MemoryType.Episodic);
```

**Solution:**
```csharp
// Classify appropriately
var type = content.Contains("how to")
    ? MemoryType.Procedural
    : MemoryType.Episodic;
await memory.StoreAsync(userId, content, type: type);
```

### ❌ Anti-Pattern 3: Synchronous Calls in Hot Paths

**Problem:**
```csharp
// Blocking the request thread
var results = memory.RecallAsync(userId, query).Result;  // Deadlock risk
```

**Solution:**
```csharp
// Async all the way
var results = await memory.RecallAsync(userId, query);
```

### ❌ Anti-Pattern 4: No Error Handling

**Problem:**
```csharp
await memory.StoreAsync(userId, content);  // What if it fails?
```

**Solution:**
```csharp
try
{
    await memory.StoreAsync(userId, content);
}
catch (MemoryStorageException ex)
{
    _logger.LogError(ex, "Failed to store memory for {UserId}", userId);
    // Fallback: queue for retry or use circuit breaker
}
```

### ❌ Anti-Pattern 5: Ignoring Deduplication

**Problem:**
```csharp
// Storing duplicates
foreach (var item in items)
{
    await memory.StoreAsync(userId, item.Content);  // May create duplicates
}
```

**Solution:**
```csharp
// Deduplication is automatic in SDK, but you can pre-check:
var existing = await memory.RecallAsync(userId, item.Content, limit: 1);
if (existing.FirstOrDefault()?.Score < 0.95f)  // Not exact duplicate
{
    await memory.StoreAsync(userId, item.Content);
}
```

---

## Performance Optimization

### 1. Batch Operations

**❌ Slow:**
```csharp
foreach (var item in items)
{
    await memory.StoreAsync(userId, item);  // 100 DB calls
}
```

**✅ Fast:**
```csharp
// Not yet supported, but plan for:
await memory.StoreBatchAsync(userId, items);  // 1 DB call
```

**Current Workaround:**
```csharp
var tasks = items.Select(item => memory.StoreAsync(userId, item));
await Task.WhenAll(tasks);  // Parallel execution
```

### 2. Embedding Cache Configuration

```csharp
services.AddMemoryIndexer(options =>
{
    options.Latency.EmbeddingCacheEnabled = true;
    options.Latency.EmbeddingCacheSize = 1000;      // Cache 1000 embeddings
    options.Latency.EmbeddingCacheTtlMinutes = 60;  // 1 hour TTL
});
```

**Impact**: 70-90% cache hit rate → 10x faster recall for repeated queries.

### 3. Query Optimization

**❌ Slow:**
```csharp
var results = await memory.RecallAsync(userId, query, limit: 100);  // Retrieve too many
```

**✅ Fast:**
```csharp
var results = await memory.RecallAsync(
    userId,
    query,
    limit: 10,  // Only what you need
    types: new[] { MemoryType.Fact, MemoryType.Semantic }  // Type filter
);
```

### 4. SQLite Auto-Management Tuning

**For write-heavy workloads:**
```json
{
  "Storage": {
    "Sqlite": {
      "MaintenanceIntervalMinutes": 60,      // Less frequent maintenance
      "CheckpointIntervalMinutes": 5,        // More frequent checkpoints
      "IncrementalVacuumPages": 500          // Larger vacuum chunks
    }
  }
}
```

**For read-heavy workloads:**
```json
{
  "Storage": {
    "Sqlite": {
      "CacheSizeKb": 10000,                  // Larger page cache (10MB)
      "MaintenanceIntervalMinutes": 30       // More frequent optimization
    }
  }
}
```

---

## Production Checklist

### Pre-Deployment

- [ ] **Configuration validated** (all API keys from environment variables)
- [ ] **Connection strings** tested and secured
- [ ] **Health checks** registered (`/health/ready`, `/health/live`)
- [ ] **Observability** configured (OpenTelemetry export)
- [ ] **Storage migration** tested on staging data
- [ ] **Backup strategy** defined (SQLite: file backup, Qdrant: snapshot)
- [ ] **Resource limits** configured (memory, CPU, storage)
- [ ] **Secrets rotation** process documented

### Deployment Verification

- [ ] **Health endpoints** responding (200 OK)
- [ ] **Embedding service** connectivity verified
- [ ] **Vector store** connectivity verified
- [ ] **MCP tools** functional (if using MCP server)
- [ ] **Metrics** flowing to monitoring system
- [ ] **Logs** captured and searchable

### Post-Deployment Monitoring

**Key Metrics to Monitor:**
- Memory store operations/sec
- Recall latency (p50, p95, p99)
- Embedding generation latency
- Cache hit rate (target: >70%)
- Deduplication rate (target: 30-40%)
- Memory count per user
- Database size growth rate

**Alerting Thresholds:**
- Recall latency p95 > 500ms
- Error rate > 1%
- Database size > 90% of limit
- Health check failures
- Embedding service timeout rate > 5%

### Operational Procedures

**Daily:**
- [ ] Review error logs
- [ ] Check memory growth trends
- [ ] Verify backup completion

**Weekly:**
- [ ] Review performance metrics
- [ ] Analyze slow queries
- [ ] Check deduplication effectiveness

**Monthly:**
- [ ] Test disaster recovery
- [ ] Review and update documentation
- [ ] Security patch updates

---

## Testing Strategies

### Unit Testing

```csharp
[Fact]
public async Task StoreAsync_ShouldStoreMemory()
{
    // Arrange
    var mockStore = new Mock<IMemoryStore>();
    var service = new MemoryService(mockStore.Object, ...);

    // Act
    var result = await service.StoreAsync("user1", "test content");

    // Assert
    Assert.NotNull(result);
    mockStore.Verify(x => x.AddAsync(It.IsAny<MemoryUnit>(), default), Times.Once);
}
```

### Integration Testing

```csharp
[Fact]
public async Task EndToEnd_StoreAndRecall()
{
    // Use InMemory providers for fast tests (InMemory is the default)
    var services = new ServiceCollection();
    services.AddMemoryIndexer(options =>
    {
        options.Embedding.Provider = EmbeddingProvider.Mock;
    });

    var provider = services.BuildServiceProvider();
    var memory = provider.GetRequiredService<MemoryService>();

    // Store
    await memory.StoreAsync("user1", "I like apples", importance: 0.8f);

    // Recall
    var results = await memory.RecallAsync("user1", "fruit preferences");

    // Assert
    Assert.Single(results);
    Assert.Contains("apples", results[0].Memory.Content);
}
```

### Load Testing

```csharp
// Use NBomber for load testing
var scenario = Scenario.Create("memory_operations", async context =>
{
    var step1 = await Step.Run("store", context, async () =>
    {
        await memory.StoreAsync($"user{context.ScenarioInfo.ThreadId}",
            $"Test content {context.InvocationNumber}");
        return Response.Ok();
    });

    var step2 = await Step.Run("recall", context, async () =>
    {
        await memory.RecallAsync($"user{context.ScenarioInfo.ThreadId}", "test");
        return Response.Ok();
    });

    return Response.Ok();
})
.WithLoadSimulations(
    Simulation.RampingInject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(5))
);

NBomberRunner.RegisterScenarios(scenario).Run();
```

---

## Common Pitfalls & Solutions

| Pitfall | Symptom | Solution |
|---------|---------|----------|
| **Out of Memory** | High RAM usage, crashes | Enable lazy loading, reduce WorkingMemory capacity |
| **Slow Recalls** | p95 > 500ms | Enable embedding cache, optimize queries, check indexes |
| **Too Many Duplicates** | 20%+ duplication rate | Lower deduplication threshold, check content quality |
| **Low Recall Quality** | Irrelevant results | Improve importance scoring, use metadata filters |
| **Database Growth** | Unbounded size | Enable auto-cleanup, configure retention policies |
| **Embedding Timeouts** | Frequent 504 errors | Increase timeout, use local embeddings, add circuit breaker |

---

## Further Reading

- [Architecture Deep Dive](ARCHITECTURE.md) (includes 3-axis model and tier/type details)
- [Integration Examples](INTEGRATIONS.md)
