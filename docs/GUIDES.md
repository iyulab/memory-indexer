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

Memory Indexer uses `MemoryIndexerOptions` (section `MemoryIndexer`) for configuration, plus the tier services' own
sections under `MemoryIndexer:VCM`. Below are the sections this guide covers, with their default values:

```json
{
  "MemoryIndexer": {
    "DefaultUserId": "default",
    "Storage": {
      "Type": "InMemory",
      "ConnectionString": "memory.db"
    },
    "Embedding": {
      "Provider": "Custom",
      "Model": "nomic-embed-text",
      "Dimensions": 768
    },
    "Completion": {
      "Provider": "None"
    },
    "SensoryBuffer": {
      "IdleTimeout": "00:01:00",
      "TokenThreshold": 500,
      "TurnThreshold": 3
    },
    "ArchivePromotion": {
      "MinConfirmationCount": 3,
      "MinConfidenceThreshold": 0.8
    },
    "VCM": {
      "WorkingMemory": {
        "SlidingExpiration": "00:30:00"
      },
      "SemanticStore": {
        "ConfidenceBoostPerConfirmation": 0.1,
        "MaxEntriesPerUser": 500
      }
    },
    "WorkingMemory": {
      "Capacity": 9,
      "EnableCapacityEnforcement": true,
      "IdleTimeout": "00:10:00",
      "TokenThreshold": 2000,
      "TurnThreshold": 10,
      "EnableTopicChangeDetection": true,
      "TopicChangeSimilarityThreshold": 0.5,
      "SummarizeBeforeArchival": true
    },
    "Intelligence": {
      "ClassificationEnabled": true
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
| | ConnectionString | string | "memory.db" | Database path for SqliteVec |
| **Embedding** | Provider | string | "Custom" | `Mock` (tests, local development) or `Custom` — the application's own `IEmbeddingService` (the library builds no embedding client; the bundled MCP server supplies an LMSupply model) |
| | Dimensions | int | 768 | Vector dimensions (must match your embedding model) |
| **Completion** | Provider | string | "None" | Used only when the application registers no `ITextCompletionService` of its own (the library builds no LLM client — register one that wraps your model, before or after `AddMemoryIndexer()`). `None`: no completion — merge/summarize and virtual-context consolidation use their non-LLM fallbacks; fact/knowledge extraction and conflict detection return empty or default results and log why. `Mock`: fixed placeholder text, for tests only (a merge would store it as memory content). `Custom` without a registration throws when the service is resolved |
| **WorkingMemory** | Capacity | int | 9 | Short tier capacity (7±2 rule); excess items are promoted to Long when `EnableCapacityEnforcement` is true |
| | IdleTimeout | TimeSpan | 00:10:00 | Archive working memory to Long after this idle time |
| | TokenThreshold | int | 2000 | Archive when accumulated tokens reach this count |
| | TurnThreshold | int | 10 | Archive after N turns |
| | EnableTopicChangeDetection | bool | true | Whether a topic change (similarity below `TopicChangeSimilarityThreshold`, default 0.5) triggers archival |
| | SummarizeBeforeArchival | bool | true | Write an extractive session summary when archiving |
| **Intelligence** | ClassificationEnabled | bool | true | Classify a new memory whose type or importance was not given |
| **SensoryBuffer** | IdleTimeout | TimeSpan | 00:01:00 | Promote to Short after idle timeout |
| | TokenThreshold | int | 500 | Promote when buffer exceeds token count |
| | TurnThreshold | int | 3 | Promote after N turns |
| **ArchivePromotion** | MinConfirmationCount | int | 3 | Required confirmations for Long → Archive promotion |
| | MinConfidenceThreshold | float | 0.8 | Required confidence for Long → Archive promotion |
| **VCM.WorkingMemory** | SlidingExpiration | TimeSpan | 00:30:00 | Evict an idle Short-tier entry after this time (its capacity is `WorkingMemory.Capacity`) |
| **VCM.SemanticStore** | MaxEntriesPerUser | int | 500 | Archive entries kept per user |

### DI Registration Patterns

**Basic (InMemory + Mock Embedding):**
```csharp
// InMemory storage is the default; Mock embeddings are deterministic but not semantic (development, tests).
// Without this line the default provider is Custom: register your own IEmbeddingService (next pattern).
services.AddMemoryIndexer(options => options.Embedding.Provider = EmbeddingProvider.Mock);
```

**With External Embedding Service:**
```csharp
// 1. Register your embedding service FIRST. Memories are embedded with GenerateEmbeddingAsync and queries with
//    GenerateQueryEmbeddingAsync (defaults to the former); a model with a query convention overrides the latter.
services.AddSingleton<IEmbeddingService, MyOpenAIEmbeddingService>();

// 2. Then add Memory Indexer — the stores size their vectors from your service's Dimensions
services.AddMemoryIndexer();
```

**With SQLite Persistent Storage:**
```csharp
services.AddMemoryIndexer(options => options.Storage.ConnectionString = "myapp_memories.db").WithSqliteVec();
```

**With Custom IMemoryStore:**
```csharp
// 1. Register your custom store
services.AddSingleton<IMemoryStore, MyPostgresMemoryStore>();

// 2. Add Memory Indexer (it will use your registered store)
services.AddMemoryIndexer();
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

`AddMemoryIndexer()` checks the options when they are first built — at host start — with the registered
`IConfigurationValidator` (dimension mismatches, thresholds out of range, contradictory settings). An error stops the
host with an `OptionsValidationException` that names each setting (`MemoryIndexer:Embedding:Dimensions: Embedding
dimensions must be positive (current: 0, expected: > 0)`); a warning is logged. Without a host, the first
`IOptions<MemoryIndexerOptions>.Value` read throws the same exception. To see the result yourself — in a health
endpoint, say — call the validator:

```csharp
var options = serviceProvider.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value;
var validation = serviceProvider.GetRequiredService<IConfigurationValidator>().Validate(options);

foreach (var warning in validation.Warnings)
    Console.WriteLine($"{warning.PropertyPath}: {warning.Message} ({warning.Suggestion})");
```

Register your own `IConfigurationValidator` before `AddMemoryIndexer()` to change the rules.

---

## Custom IMemoryStore Implementation

Memory Indexer provides `IMemoryStore` interface for custom storage backends. This enables integration with any database or vector store (PostgreSQL, Qdrant, Redis, Pinecone, etc.).

### Interface Overview

```csharp
public interface IMemoryStore
{
    // Core CRUD - the user id is part of the key: another user's memory with the same id is not found,
    // updated, or deleted, and UpdateAsync only updates a row stored under memory.UserId
    Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(IEnumerable<MemoryUnit> memories, CancellationToken ct = default);
    Task<MemoryUnit?> GetByIdAsync(string userId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(string userId, IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken ct = default);
    Task<bool> DeleteAsync(string userId, Guid id, bool hardDelete = false, CancellationToken ct = default);

    // Bulk operations
    Task<int> DeleteByUserAsync(string userId, bool hardDelete = false, CancellationToken ct = default);
    Task<int> DeleteBySessionAsync(string userId, string sessionId, bool hardDelete = false, CancellationToken ct = default);
    Task<int> DeleteByNamespaceAsync(string userId, string namespaceName, bool hardDelete = false, CancellationToken ct = default);

    // Search & retrieval - MemorySearchOptions.UserId is required; throw ArgumentException when it is empty
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, MemorySearchOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryUnit>> GetAllAsync(string userId, MemoryFilterOptions? options = null, CancellationToken ct = default);

    // Statistics
    Task<long> GetCountAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUserIdsAsync(CancellationToken ct = default);
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
        string userId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        // Option 1: Default implementation (iterates GetByIdAsync for this user)
        return this.GetByIdsDefaultAsync(userId, ids, ct);

        // Option 2: Native multi-get (recommended)
        // return _db.Memories.Where(m => m.UserId == userId && ids.Contains(m.Id)).ToListAsync(ct);
    }

    // ... the other IMemoryStore members
}
```

### Available Extension Methods

| Method | Purpose |
|--------|---------|
| `PrepareForStore()` | Sets Id (if empty), CreatedAt, UpdatedAt |
| `ValidateForStore()` | Throws if UserId or Content is missing |
| `ValidateSearchOptions()` | Validates search parameters (throws if UserId is empty) |
| `StoreBatchDefaultAsync()` | Default batch store (iterates StoreAsync) |
| `GetByIdsDefaultAsync()` | Default multi-get for one user (iterates GetByIdAsync) |
| `ApplyFilter()` | LINQ filter for MemoryFilterOptions |
| `ApplySearchFilter()` | LINQ filter for MemorySearchOptions (always scoped to UserId) |
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

    // ... the other IMemoryStore members
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

**Pattern**: Store each turn with its role, recall by relevance

```csharp
public class ConversationService
{
    private readonly IMemoryService _memory;

    public ConversationService(IMemoryService memory) => _memory = memory;

    public async Task ProcessUserMessage(string userId, string sessionId, string message)
    {
        // The memory type is classified automatically; the role is kept on episodic memories.
        await _memory.RememberAsync(userId, sessionId, message, role: "user");
    }

    public async Task<string> GetConversationContext(string userId, string sessionId, string query)
    {
        var context = await _memory.RecallAsync(userId, sessionId, query, limit: 10);
        return string.Join("\n\n", context.AllMemories().Select(m =>
            $"[{m.Role ?? "unknown"}]: {m.Content}"));
    }
}
```

### 2. User Preference Learning

**Pattern**: Gradual confidence building with confirmation tracking

```csharp
public class PreferenceService
{
    private readonly IMemoryPrimitives _memory;

    public PreferenceService(IMemoryPrimitives memory) => _memory = memory;

    public async Task LearnPreference(string userId, string preference)
    {
        await _memory.EncodeAsync(new EncodeRequest
        {
            UserId = userId,
            Content = preference,
            Type = MemoryType.Fact,
            ImportanceScore = 0.7f,
            Metadata = new() { ["category"] = "preference" }
        });

        // Automatically promotes to User Profile when:
        // - Mentioned 3+ times AND
        // - Confidence ≥ 0.8
    }

    public async Task<List<string>> GetUserPreferences(string userId)
    {
        var prefs = await _memory.RetrieveAsync(new RetrieveRequest
        {
            UserId = userId,
            Query = "user preferences",
            Types = [MemoryType.Fact],
            Limit = 10
        });

        return prefs
            .Where(r => r.Memory.Metadata?.GetValueOrDefault("category") == "preference")
            .Select(r => r.Memory.Content)
            .ToList();
    }
}
```

### 3. Entity Relationship Tracking

**Pattern**: Link a memory to the entities it mentions, then traverse from it

```csharp
public class RelationshipTracker
{
    private readonly IMemoryGraphService _graph;

    public RelationshipTracker(IMemoryGraphService graph) => _graph = graph;

    public async Task TrackRelationship(
        MemoryUnit memory,
        string entity1,
        string entity2,
        string relationship)
    {
        var triple = new EntityTriple
        {
            UserId = memory.UserId,
            Subject = entity1,
            Predicate = relationship,
            ObjectValue = entity2,
            Confidence = 0.9f,
            SourceMemoryId = memory.Id
        };

        await _graph.LinkMemoryToGraphAsync(memory, [triple]);
    }

    public async Task<List<string>> FindRelatedMemories(string userId, Guid memoryId)
    {
        var related = await _graph.FindRelatedMemoriesAsync(
            userId,
            memoryId,
            maxHops: 2,
            topK: 10);

        return related.Select(r => r.Memory.Content).ToList();
    }
}
```

### 4. Session Continuity

**Pattern**: Session-scoped memory with automatic summarization

```csharp
public class SessionManager
{
    private readonly IMemoryPrimitives _memory;
    private readonly IMemoryStore _store;

    public SessionManager(IMemoryPrimitives memory, IMemoryStore store) => (_memory, _store) = (memory, store);

    public async Task StartSession(string userId, string sessionId)
    {
        await _memory.EncodeAsync(new EncodeRequest
        {
            UserId = userId,
            SessionId = sessionId,
            Content = $"Session started: {sessionId}",
            Type = MemoryType.Episodic,
            ImportanceScore = 0.3f
        });
    }

    public async Task<string> GetSessionSummary(string userId, string sessionId)
    {
        var memories = await _store.GetAllAsync(
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
    private readonly IChatClient _chat;   // your model, through Microsoft.Extensions.AI

    public ChatService(IContextBuilder contextBuilder, IChatClient chat) => (_contextBuilder, _chat) = (contextBuilder, chat);

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
        var response = await _chat.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, $"Context from memory:\n{bundle.Content}"),
            new ChatMessage(ChatRole.User, userMessage)
        ]);

        return response.Text;
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
    private readonly IReflectionEngine _reflection;   // registered by AddMemoryIndexer()
    private readonly IMemoryStore _store;

    public async Task PerformDailyReflection(string userId, CancellationToken ct)
    {
        // Is there enough new, important material to reflect on?
        var trigger = await _reflection.ShouldReflectAsync(userId, ct);
        if (!trigger.ShouldReflect)
            return;

        var result = await _reflection.ReflectAsync(
            userId,
            new ReflectionOptions { TimeWindow = TimeSpan.FromDays(7), MinImportance = 0.7f },
            ct);

        // Reflection returns insights; it does not store them. Keep the ones you want as memories.
        foreach (var insight in result.Insights)
        {
            await _store.StoreAsync(new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Content = insight.Content,
                Type = MemoryType.Semantic,
                Tier = Tier.Long
            }, ct);
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
      "Provider": "Custom",       // your IEmbeddingService - e.g. one that calls your Ollama server
      "Model": "bge-m3",
      "Dimensions": 1024
    }
  }
}
```

**Production:**
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",          // or register your own IMemoryStore (see "Custom IMemoryStore Implementation")
      "ConnectionString": "/var/lib/memory-indexer/memory.db",
      "Sqlite": { "EnableAutoMaintenance": true }
    },
    "Embedding": {
      "Provider": "Custom",       // your IEmbeddingService holds its endpoint and key
      "Model": "text-embedding-3-large",
      "Dimensions": 3072
    },
    "WorkingMemory": { "Capacity": 10 }   // Larger for production (Baddeley's 7±2 is the 5–11 the validator accepts)
  }
}
```

### Secrets Management

The library builds no embedding or completion client, so it holds no API key: the key belongs to the
`IEmbeddingService` / `IChatClient` you register.

**❌ Wrong:**
```csharp
var apiKey = "sk-1234567890abcdef";  // Hardcoded: in the binary and in source control
```

**✅ Correct:**
```csharp
// Environment variables, the .NET Secret Manager (development) and a vault provider such as Azure Key Vault
// (production) all surface through IConfiguration, so the code reads the key the same way everywhere
var apiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured");
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

> In these snippets `memory` is an `IMemoryService`, `primitives` an `IMemoryPrimitives` and `store` an `IMemoryStore`, all resolved from DI;
> `userId`, `content`, `query` and `items` (a list of texts) are the caller's strings, and `_logger` its `ILogger`.

### ❌ Anti-Pattern 1: Storing Everything

**Problem:**
```csharp
// Storing every minor detail
await memory.RememberAsync(userId, null, "User typed 'a'");
await memory.RememberAsync(userId, null, "User backspaced");
```

**Solution:**
```csharp
// Store only meaningful interactions
await memory.RememberAsync(userId, null, "User searched for 'machine learning tutorials'");
```

### ❌ Anti-Pattern 2: Ignoring Memory Types

**Problem:**
```csharp
// Everything as Episodic
await primitives.EncodeAsync(new EncodeRequest { UserId = userId, Content = content, Type = MemoryType.Episodic });
```

**Solution:**
```csharp
// Classify appropriately
var type = content.Contains("how to")
    ? MemoryType.Procedural
    : MemoryType.Episodic;
await primitives.EncodeAsync(new EncodeRequest { UserId = userId, Content = content, Type = type });
```

### ❌ Anti-Pattern 3: Synchronous Calls in Hot Paths

**Problem:**
```csharp
// Blocking the request thread
var results = memory.RecallAsync(userId, sessionId: null, query).Result;  // Deadlock risk
```

**Solution:**
```csharp
// Async all the way
var results = await memory.RecallAsync(userId, sessionId: null, query);
```

### ❌ Anti-Pattern 4: No Error Handling

**Problem:**
```csharp
await memory.RememberAsync(userId, null, content);  // What if it fails?
```

**Solution:**
```csharp
try
{
    await memory.RememberAsync(userId, null, content);
}
catch (Exception ex) when (ex is not OperationCanceledException)
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
    await memory.RememberAsync(userId, null, item);  // May create duplicates
}
```

**Solution:**
```csharp
// Identical content is skipped automatically (ignoring case and whitespace); similar content is stored with a relation.
// To also skip near-duplicates, pre-check — or set DeduplicationOptions.ActOnSimilarity with thresholds calibrated
// for your embedder:
foreach (var item in items)
{
    var existing = await primitives.RetrieveAsync(new RetrieveRequest { UserId = userId, Query = item, Limit = 1 });
    if (existing.FirstOrDefault() is not { Score: >= 0.95f })  // Not an exact duplicate
    {
        await memory.RememberAsync(userId, null, item);
    }
}
```

---

## Performance Optimization

### 1. Batch Operations

**❌ Slow:**
```csharp
foreach (var item in items)
{
    await store.StoreAsync(new MemoryUnit { UserId = userId, Content = item });  // 100 DB calls
}
```

**✅ Fast:**
```csharp
// IMemoryStore.StoreBatchAsync — a store may implement it natively (the default iterates StoreAsync)
await store.StoreBatchAsync(items.Select(item => new MemoryUnit { UserId = userId, Content = item }));
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
var results = await primitives.RetrieveAsync(new RetrieveRequest { UserId = userId, Query = query, Limit = 100 });  // Retrieve too many
```

**✅ Fast:**
```csharp
var results = await primitives.RetrieveAsync(new RetrieveRequest
{
    UserId = userId,
    Query = query,
    Limit = 10,                                          // Only what you need
    Types = [MemoryType.Fact, MemoryType.Semantic]       // Type filter
});
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

Test your own code against the library's interfaces — here the `ConversationService` from
[Conversation History Management](#1-conversation-history-management), with an NSubstitute substitute for `IMemoryService`:

```csharp
using NSubstitute;
using Xunit;

public class ConversationServiceTests
{
    [Fact]
    public async Task ProcessUserMessage_RemembersTheTurnWithItsRole()
    {
        // Arrange
        var memory = Substitute.For<IMemoryService>();
        var conversations = new ConversationService(memory);

        // Act
        await conversations.ProcessUserMessage("user1", "session1", "I like apples");

        // Assert
        await memory.Received(1).RememberAsync("user1", "session1", "I like apples", role: "user");
    }
}
```

### Integration Testing

```csharp
using Xunit;

public class MemoryIndexerIntegrationTests
{
    [Fact]
    public async Task EndToEnd_RememberAndRecall()
    {
        // Use InMemory providers for fast tests (InMemory is the default); the Mock embedder needs no model
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(options =>
        {
            options.Embedding.Provider = EmbeddingProvider.Mock;
        });

        await using var provider = services.BuildServiceProvider();
        var memory = provider.GetRequiredService<IMemoryService>();

        // Store
        await memory.RememberAsync("user1", "session1", "I like apples");

        // Recall (the Mock embedder is not semantic: query with the stored text)
        var context = await memory.RecallAsync("user1", "session1", "I like apples");

        // Assert
        Assert.Contains(context.AllMemories(), m => m.Content.Contains("apples"));
    }
}
```

### Load Testing

```csharp
// Use NBomber for load testing
var scenario = Scenario.Create("memory_operations", async context =>
{
    var step1 = await Step.Run("store", context, async () =>
    {
        await memory.RememberAsync($"user{context.ScenarioInfo.ThreadId}", sessionId: null,
            $"Test content {context.InvocationNumber}");
        return Response.Ok();
    });

    var step2 = await Step.Run("recall", context, async () =>
    {
        await memory.RecallAsync($"user{context.ScenarioInfo.ThreadId}", sessionId: null, "test");
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
