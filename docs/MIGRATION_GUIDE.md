# Migration Guide

This guide covers migrating Memory Indexer between different storage backends and versions.

## Storage Backend Migration

### InMemory to SQLite-vec

For development to local persistence:

```csharp
// Before: InMemory (development)
options.Storage.Type = StorageType.InMemory;

// After: SQLite-vec (local persistence)
options.Storage.Type = StorageType.SqliteVec;
options.Storage.ConnectionString = "memories.db";
options.Storage.Sqlite = new SqliteOptions
{
    UseWalMode = true,
    EnableVectorSearch = true,
    EnableFullTextSearch = true
};
```

**Configuration (appsettings.json):**
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "data/memories.db",
      "Sqlite": {
        "UseWalMode": true,
        "EnableVectorSearch": true,
        "EnableFullTextSearch": true
      }
    }
  }
}
```

### SQLite-vec to Qdrant

For production scalability:

```csharp
// Before: SQLite-vec (local)
options.Storage.Type = StorageType.SqliteVec;
options.Storage.ConnectionString = "memories.db";

// After: Qdrant (production)
options.Storage.Type = StorageType.Qdrant;
options.Storage.ConnectionString = "localhost:6334";
options.Storage.CollectionName = "memories";
options.Storage.Qdrant = new QdrantOptions
{
    ApiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY")
};
```

**Configuration (appsettings.Production.json):**
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "Qdrant",
      "ConnectionString": "your-qdrant-host:6334",
      "CollectionName": "memories",
      "Qdrant": {
        "ApiKey": "${QDRANT_API_KEY}"
      }
    }
  }
}
```

### Data Migration Script

Use the built-in migrator for data transfer:

```csharp
using MemoryIndexer.Storage.Migration;

// Create migrator
var migrator = new MemoryStoreMigrator(
    sourceStore: sqliteStore,
    targetStore: qdrantStore,
    logger: logger);

// Migrate all data
var result = await migrator.MigrateAsync(
    batchSize: 100,
    cancellationToken: ct);

Console.WriteLine($"Migrated: {result.SuccessCount} memories");
Console.WriteLine($"Failed: {result.FailureCount}");
```

## Embedding Provider Migration

### Mock to Local Embeddings

```csharp
// Before: Mock (testing)
options.Embedding.Provider = EmbeddingProvider.Mock;
options.Embedding.Dimensions = 1024;

// After: Local ONNX model
options.Embedding.Provider = EmbeddingProvider.Local;
options.Embedding.Model = "all-MiniLM-L6-v2";
options.Embedding.Dimensions = 384;
```

**Important:** When changing embedding dimensions, you must re-embed all stored memories.

### Local to Ollama

```csharp
// Before: Local
options.Embedding.Provider = EmbeddingProvider.Local;
options.Embedding.Model = "all-MiniLM-L6-v2";
options.Embedding.Dimensions = 384;

// After: Ollama (for better quality)
options.Embedding.Provider = EmbeddingProvider.Ollama;
options.Embedding.Endpoint = "http://localhost:11434";
options.Embedding.Model = "bge-m3";
options.Embedding.Dimensions = 1024;
```

### Re-embedding Memories

When changing embedding models, regenerate all embeddings:

```csharp
public async Task ReembedAllMemoriesAsync(
    IMemoryStore store,
    IEmbeddingService oldEmbedding,
    IEmbeddingService newEmbedding)
{
    var allMemories = await store.GetAllAsync(new MemorySearchOptions { Limit = int.MaxValue });

    foreach (var memory in allMemories)
    {
        // Generate new embedding
        var newVector = await newEmbedding.GenerateEmbeddingAsync(memory.Content);

        // Update memory with new embedding
        memory.Embedding = newVector;
        await store.UpdateAsync(memory);
    }
}
```

## Version Migration

### v0.1.x to v0.2.x (Future)

Breaking changes expected in v0.2:
- `MemoryUnit.Tags` → `MemoryUnit.Topics`
- `IMemoryStore.SearchByText()` removed (use `SearchAsync()`)

Migration steps:
1. Update NuGet package
2. Replace deprecated APIs
3. Run schema migration (if using SQLite)

## Environment-Specific Configuration

### Development
```json
{
  "MemoryIndexer": {
    "Storage": { "Type": "InMemory" },
    "Embedding": { "Provider": "Local" }
  }
}
```

### Staging
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "staging-memories.db"
    },
    "Embedding": {
      "Provider": "Ollama",
      "Endpoint": "http://ollama-staging:11434"
    }
  }
}
```

### Production
```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "Qdrant",
      "ConnectionString": "qdrant-cluster:6334",
      "Qdrant": { "ApiKey": "${QDRANT_API_KEY}" }
    },
    "Embedding": {
      "Provider": "OpenAI",
      "Endpoint": "https://api.openai.com",
      "ApiKey": "${OPENAI_API_KEY}",
      "Model": "text-embedding-3-small"
    }
  }
}
```

## Troubleshooting

### Embedding Dimension Mismatch

**Error:** `Vector dimension mismatch: expected 1024, got 384`

**Solution:** Ensure storage and embedding dimensions match:
```csharp
options.Storage.VectorDimensions = 384;  // Must match embedding
options.Embedding.Dimensions = 384;
```

### Connection Issues

**Qdrant connection failed:**
```bash
# Check Qdrant is running
curl http://localhost:6334/collections

# Check API key if configured
export QDRANT_API_KEY=your-key
```

**Ollama embedding failed:**
```bash
# Check Ollama is running
ollama list

# Pull embedding model if missing
ollama pull bge-m3
```

### Performance Issues

**Slow embedding generation:**
- Enable caching: `options.Embedding.CacheTtlMinutes = 60`
- Use batch processing for bulk operations
- Consider local embeddings for low-latency requirements

**Slow search queries:**
- Reduce `options.Search.DefaultLimit`
- Enable hybrid search: `options.Search.DenseWeight = 0.7`
- For Qdrant: ensure proper indexing configuration

## Best Practices

1. **Always backup data before migration**
2. **Test migrations in staging first**
3. **Use environment variables for secrets**
4. **Monitor performance after migration**
5. **Keep embedding dimensions consistent**
