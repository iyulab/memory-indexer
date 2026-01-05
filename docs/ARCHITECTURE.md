# Architecture

## Project Structure (v0.3.0)

```
src/
├── MemoryIndexer/               # Core abstractions (lightweight package)
│   ├── Interfaces/              # Core contracts
│   ├── Models/                  # Domain entities
│   ├── Services/                # Core orchestration
│   ├── InMemory/                # Dev/test implementations
│   ├── Mock/                    # Mock services
│   ├── Scoring/                 # Basic scoring
│   └── Configuration/           # Options and settings
│
└── MemoryIndexer.Sdk/           # Full implementation (heavy package)
    ├── Storage/                 # Sqlite, Qdrant
    ├── Embedding/               # Local, Ollama, OpenAI
    ├── Intelligence/            # All AI/ML features
    ├── Mcp/                     # MCP tools
    ├── Observability/           # OpenTelemetry
    └── Extensions/              # DI registration

tools/
└── McpServer/                   # Standalone MCP server

tests/
├── MemoryIndexer.Tests/         # Core tests
└── MemoryIndexer.Sdk.Tests/     # SDK tests

samples/
├── TwentyQuestionsGame/         # Memory-only context demo
└── MemoryChatApp/               # Web frontend sample
```

## 4-Tier Virtual Context Management

Memory Indexer implements a 4-tier memory architecture inspired by human cognitive systems:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           USER INPUT                                      │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  RECENTLY BUFFER (Tier 0)                                                │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Raw conversation staging • Full text • Async processing          │  │
│  │ TTL: 60s idle | 500 tokens | 3 turns (OR logic)                  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ BufferPromoter
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  WORKING MEMORY (L1)                                                     │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Topic-grouped • Summarized chunks • Active context               │  │
│  │ Capacity: 4-7 items • TTL: 10min | 2K tokens | topic change      │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ WorkingMemoryOrchestrator
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  SESSION MEMORY (L2)                                                     │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Session summaries • Extracted facts • Compressed representation  │  │
│  │ Storage: Vector DB (Qdrant/SQLite-vec)                           │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ AND logic promotion
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  USER PROFILE (L3)                                                       │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Long-term facts • Preferences • Identity • Cross-session          │  │
│  │ Promotion: Confidence >= 0.8 AND Confirmations >= 3               │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Tier Interfaces

| Tier | Interface | Implementation |
|------|-----------|----------------|
| Recently | `IRecentlyBuffer` | `RecentlyBufferService` |
| Working | `IWorkingMemory` | `WorkingMemoryService` |
| Session | `ISessionStore` | `InMemorySessionStore` |
| User | `IUserProfile` | `UserProfileService` |

### Promotion Services

| Transition | Interface | Implementation |
|------------|-----------|----------------|
| Recently → Working | `IBufferPromoter` | `BufferPromoterService` |
| Working → Session | `IWorkingMemoryOrchestrator` | `WorkingMemoryOrchestratorService` |

## Layer Diagram

```
┌─────────────────────────────────────────────────────┐
│  MCP Interface Layer                                │
│  MemoryTools, AdvancedMemoryTools, KnowledgeGraph   │
│  using [McpServerTool] attributes                   │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  Orchestration Layer                                │
│  VirtualContextManager: coordinates all tiers       │
│  MemoryPrimitives: 12 fundamental operations        │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  4-Tier Memory Layer                                │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐   │
│  │Recently │→│ Working │→│ Session │→│  User   │   │
│  │ Buffer  │ │  Memory │ │  Store  │ │ Profile │   │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘   │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  Intelligence Layer                                 │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐      │
│  │ Classifier │ │ Summarizer │ │ Reranker   │      │
│  └────────────┘ └────────────┘ └────────────┘      │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐      │
│  │ Deduplicat │ │ Conflict   │ │ Entity     │      │
│  │   -or      │ │ Resolver   │ │ Extractor  │      │
│  └────────────┘ └────────────┘ └────────────┘      │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  Infrastructure Layer                               │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐      │
│  │  Embedding │ │  Storage   │ │  Scoring   │      │
│  │  Service   │ │   Store    │ │  Service   │      │
│  └────────────┘ └────────────┘ └────────────┘      │
│  Providers: Ollama, OpenAI, Local, SQLite, Qdrant  │
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

    public MemoryType Type { get; set; }      // Episodic, Semantic, Procedural, Fact
    public MemoryTier Tier { get; set; }      // Working, Session, User
    public float ImportanceScore { get; set; }
    public int AccessCount { get; set; }
}
```

### UserProfileEntry

Long-term user knowledge with confirmation tracking:

```csharp
public class UserProfileEntry
{
    public required string Key { get; init; }
    public required string Value { get; set; }
    public UserProfileCategory Category { get; set; }
    public float Confidence { get; set; }           // 0.0 - 1.0
    public int ConfirmationCount { get; set; }      // Track mentions
    public List<string> SourceSessions { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
    public ReadOnlyMemory<float>? Embedding { get; set; }

    // Promotion requires: ConfirmationCount >= 3 AND Confidence >= 0.8
    public bool IsConfirmed => ConfirmationCount >= 3 && Confidence >= 0.8f;
}
```

### RecentlyMemory

Raw buffer entry before processing:

```csharp
public record RecentlyMemory
{
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; }
    public int TokenCount { get; init; }
    public int TurnIndex { get; init; }
    public ReadOnlyMemory<float>? Embedding { get; set; }
}
```

## Promotion Triggers

### OR Logic (Lower Tiers)

Recently → Working and Working → Session use OR logic for aggressive cleanup:

```csharp
public enum RecentlyPromotionTrigger
{
    None = 0,
    IdleTimeout = 1,      // 60 seconds
    TokenThreshold = 2,   // 500 tokens
    TurnThreshold = 3,    // 3 turns
    Manual = 4
}

public enum WorkingPromotionTrigger
{
    None = 0,
    IdleTimeout = 1,      // 10 minutes
    TokenThreshold = 2,   // 2000 tokens
    TurnThreshold = 3,    // 10 turns
    TopicChange = 4,
    Manual = 5,
    SessionEnd = 6
}
```

### AND Logic (User Tier)

Session → User uses AND logic for conservative promotion:

```csharp
public class UserProfileOptions
{
    public int MinConfirmationCount { get; set; } = 3;
    public float MinConfidenceThreshold { get; set; } = 0.8f;
    public float ConfidenceBoostPerConfirmation { get; set; } = 0.1f;
    public int MaxEntriesPerUser { get; set; } = 500;
}
```

## MCP Tools

Tools registered via `[McpServerTool]` attribute:

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
    options.Storage.Type = StorageType.SqliteVec;
    options.Embedding.Provider = EmbeddingProvider.Ollama;
    options.Embedding.Dimensions = 1024;
});
```

Registers (core services):
- `MemoryService` (orchestration)
- `IMemoryStore` (based on Storage.Type)
- `IEmbeddingService` (based on Embedding.Provider)
- `IScoringService`

Registers (4-tier architecture):
- `IRecentlyBuffer` → `RecentlyBufferService`
- `IWorkingMemory` → `WorkingMemoryService`
- `IBufferPromoter` → `BufferPromoterService`
- `IWorkingMemoryOrchestrator` → `WorkingMemoryOrchestratorService`
- `IUserProfile` → `UserProfileService`

Registers (intelligence):
- `IMemoryClassifier` → `LocalMemoryClassifier`
- `ISummarizationService` → `ExtractiveSummarizer`
- `IRerankerService` → `LocalRerankerService`
- `IContradictionDetector` → `SemanticContradictionDetector`

## Vector Search

Uses `Microsoft.Extensions.VectorData.Abstractions` for backend-agnostic operations:

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
      "ConnectionString": "memory.db",
      "VectorDimensions": 1024
    },
    "Embedding": {
      "Provider": "Mock | Local | Ollama | OpenAI",
      "Model": "bge-m3",
      "Dimensions": 1024
    },
    "VCM": {
      "WorkingMemory": {
        "Capacity": 7,
        "DefaultTtl": "00:10:00"
      },
      "RecentlyBuffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "WorkingOrchestrator": {
        "IdleTimeout": "00:10:00",
        "TokenThreshold": 2000,
        "TurnThreshold": 10
      },
      "UserProfile": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8
      }
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

## Intelligence Components

### Summarization

- **ExtractiveSummarizer**: TextRank-based sentence extraction
- **RollingSummaryManager**: Incremental session summaries
- **SummarizationOrchestrator**: Strategy-based (Extractive, Compression, Hybrid)

### Entity Resolution

- **EntityExtractor**: Named entity recognition
- **CoreferenceResolver**: Pronoun resolution (he/she/it → entity)
- **TemporalEntityStore**: Time-aware entity tracking

### Conflict Detection

- **SemanticContradictionDetector**: Embedding-based contradiction detection
- **ContradictionResolver**: Resolution strategies with versioning

### Memory Operations

- **SemanticOperationDecider**: ADD/UPDATE/DELETE/MERGE decisions
- **DuplicateDetector**: Semantic deduplication
- **ImportanceAnalyzer**: Value assessment scoring
