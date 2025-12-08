# Architecture

## Project Structure

```
src/
├── MemoryIndexer.Console      # Standalone MCP server (entry point)
├── MemoryIndexer.Core         # Domain models and interfaces
├── MemoryIndexer.Embedding    # Embedding service implementations
├── MemoryIndexer.Intelligence # Scoring and chunking services
├── MemoryIndexer.Mcp          # MCP tool implementations
├── MemoryIndexer.Sdk          # NuGet package for embedding
└── MemoryIndexer.Storage      # Storage implementations

tests/
├── MemoryIndexer.Core.Tests
├── MemoryIndexer.Storage.Tests
└── MemoryIndexer.Integration.Tests
```

## Layer Diagram

```
┌─────────────────────────────────────────────────────┐
│  MCP Interface Layer                                │
│  MemoryTools: store, recall, get, list, update,     │
│  delete - using [McpServerTool] attributes          │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  Service Layer                                      │
│  MemoryService: orchestrates storage + embedding    │
│  + scoring for memory operations                    │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  Domain Layer                                       │
│  MemoryUnit, Session, EntityTriple                  │
│  IMemoryStore, IEmbeddingService, IScoringService   │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  Infrastructure Layer                               │
│  InMemoryMemoryStore, SqliteMemoryStore (planned)   │
│  MockEmbeddingService, OllamaEmbedding (planned)    │
└─────────────────────────────────────────────────────┘
```

## Core Components

### MemoryUnit

Primary entity for storing memories with vector embeddings:

```csharp
public class MemoryUnit
{
    [VectorStoreKey]
    public Guid Id { get; set; }

    [VectorStoreData]
    public string UserId { get; set; }

    [VectorStoreData]
    public string Content { get; set; }

    [VectorStoreVector(Dimensions: 1024)]
    public ReadOnlyMemory<float>? Embedding { get; set; }

    public MemoryType Type { get; set; }  // Episodic, Semantic, Procedural, Fact
    public float ImportanceScore { get; set; }
    public int AccessCount { get; set; }
}
```

### MemoryService

Orchestrates memory operations:

- **StoreAsync**: Generate embedding → Create MemoryUnit → Persist
- **RecallAsync**: Generate query embedding → Vector search → Re-rank by score
- **UpdateContentAsync**: Update content → Regenerate embedding → Persist

### MCP Tools

Tools registered via `[McpServerTool]` attribute in `MemoryTools.cs`:

| Tool | Operation |
|------|-----------|
| `memory_store` | Store with auto-embedding |
| `memory_recall` | Semantic similarity search |
| `memory_get` | Get by ID |
| `memory_list` | List with filters |
| `memory_update` | Update content/importance |
| `memory_delete` | Soft or hard delete |

## Dependency Injection

Registration via `AddMemoryIndexer()` extension:

```csharp
services.AddMemoryIndexer(options => {
    options.Storage.Type = StorageType.InMemory;
    options.Embedding.Dimensions = 1024;
});
```

Registers:
- `MemoryService` (singleton)
- `IMemoryStore` (based on Storage.Type)
- `IEmbeddingService` (based on Embedding.Provider)
- `IScoringService`

## Vector Search

Uses `Microsoft.Extensions.VectorData.Abstractions` for backend-agnostic vector operations:

1. Content → Embedding via `IEmbeddingService`
2. Store with `[VectorStoreVector]` attribute
3. Search using cosine similarity
4. Re-rank using combined score (similarity + recency + importance)

## Configuration Schema

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "InMemory | SqliteVec | Qdrant",
      "ConnectionString": "memory.db"
    },
    "Embedding": {
      "Provider": "Mock | Ollama | OpenAI",
      "Model": "bge-m3",
      "Dimensions": 1024
    },
    "Scoring": {
      "RecencyDecayFactor": 0.99,
      "ImportanceWeight": 1.0
    },
    "Search": {
      "DefaultLimit": 10,
      "MinScore": 0.0
    }
  }
}
```
