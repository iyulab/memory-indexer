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

## 4-Tier Cognitive Architecture

Memory Indexer implements a 4-tier cognitive memory architecture inspired by Atkinson-Shiffrin and Tulving's memory models:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           USER INPUT                                      │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  SENSORY BUFFER (T0)                                                     │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Raw conversation staging • Full text • Async processing          │  │
│  │ TTL: 60s idle | 500 tokens | 3 turns (OR logic)                  │  │
│  │ (Atkinson-Shiffrin sensory memory store)                         │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ BufferPromoter
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  WORKING MEMORY (T1)                                                     │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Topic-grouped • Summarized chunks • Active context               │  │
│  │ Capacity: 4-7 items • TTL: 10min | 2K tokens | topic change      │  │
│  │ (Baddeley's working memory model)                                │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ ShortTermMemoryOrchestrator
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  EPISODIC STORE (T2)                                                     │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Session experiences • Temporal events • Compressed episodes      │  │
│  │ Storage: Vector DB (Qdrant/SQLite-vec)                           │  │
│  │ (Tulving's episodic memory - event-based)                        │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ AND logic promotion
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  SEMANTIC STORE (T3)                                                     │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Long-term knowledge • Preferences • Identity • Cross-session      │  │
│  │ Promotion: Confidence >= 0.8 AND Confirmations >= 3               │  │
│  │ (Tulving's semantic memory - fact-based)                         │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Tier Interfaces

| Tier | Interface | Implementation | Cognitive Model |
|------|-----------|----------------|-----------------|
| Buffer (T0) | `IBuffer` | `BufferService` | Atkinson-Shiffrin sensory memory |
| Short-Term (T1) | `IShortTermMemory` | `ShortTermMemoryService` | Baddeley's working memory |
| Long-Term (T2) | `ILongTermStore` | `InMemoryEpisodicStore` | Tulving's episodic memory |
| Archive (T3) | `IArchiveStore` | `SemanticStoreService` | Tulving's semantic memory |

### Promotion Services

| Transition | Interface | Implementation |
|------------|-----------|----------------|
| Sensory → Working | `IBufferPromoter` | `BufferPromoterService` |
| Working → Episodic | `IShortTermMemoryOrchestrator` | `ShortTermMemoryOrchestratorService` |

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
│  4-Tier Cognitive Memory Layer                      │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐   │
│  │ Sensory │→│ Working │→│Episodic │→│Semantic │   │
│  │ Buffer  │ │  Memory │ │  Store  │ │  Store  │   │
│  │  (T0)   │ │  (T1)   │ │  (T2)   │ │  (T3)   │   │
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

### SemanticStoreEntry

Long-term knowledge with confirmation tracking (Tulving's semantic memory):

```csharp
public class SemanticStoreEntry
{
    public required string Key { get; init; }
    public required string Value { get; set; }
    public SemanticStoreCategory Category { get; set; }
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

### BufferMemory

Raw buffer entry before processing (Atkinson-Shiffrin sensory store):

```csharp
public record BufferMemory
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

Sensory → Working and Working → Episodic use OR logic for aggressive cleanup:

```csharp
public enum PromotionTriggerType
{
    None = 0,
    IdleTimeout = 1,      // Sensory: 60s, Working: 10min
    TokenThreshold = 2,   // Sensory: 500, Working: 2K
    TurnThreshold = 3,    // Sensory: 3, Working: 10
    TopicChange = 4,      // Working only
    Manual = 5,
    SessionEnd = 6        // Working only
}
```

### AND Logic (Semantic Tier)

Episodic → Semantic uses AND logic for conservative promotion:

```csharp
public class SemanticStoreOptions
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

Registers (4-tier cognitive architecture):
- `IBuffer` → `BufferService` (T0)
- `IShortTermMemory` → `ShortTermMemoryService` (T1)
- `ILongTermStore` → `InMemoryEpisodicStore` (T2)
- `IArchiveStore` → `SemanticStoreService` (T3)
- `IBufferPromoter` → `BufferPromoterService` (T0→T1)
- `IShortTermMemoryOrchestrator` → `ShortTermMemoryOrchestratorService` (T1→T2)

Registers (intelligence):
- `IMemoryClassifier` → `LocalMemoryClassifier`
- `ISummarizationService` → `ExtractiveSummarizer`
- `IRerankerService` → `LocalRerankerService`
- `IContradictionDetector` → `SemanticContradictionDetector`
- `IDeduplicationService` → `DeduplicationService`
- `IScoreNormalizer` → `AdaptiveScoreNormalizer`

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
      "ShortTermMemory": {
        "Capacity": 7,
        "DefaultTtl": "00:10:00"
      },
      "Buffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "WorkingOrchestrator": {
        "IdleTimeout": "00:10:00",
        "TokenThreshold": 2000,
        "TurnThreshold": 10
      },
      "SemanticStore": {
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
- **DeduplicationService**: Tiered semantic deduplication with content-type awareness
  - Exact duplicates (≥0.95): Skip storage
  - High similarity (0.85-0.94): Merge content
  - Medium similarity (0.75-0.84): Update existing
  - Low similarity (0.65-0.74): Add with relation
- **ImportanceAnalyzer**: Value assessment scoring

### Score Normalization

- **AdaptiveScoreNormalizer**: Auto-selects normalization strategy based on score distribution
  - Narrow spread (< 0.3): Percentile ranking for forced separation
  - High variance (CV > 0.5): Z-score for outlier handling
  - Normal distribution: MinMax linear scaling
- **MinMaxScoreNormalizer**: Linear 0-1 scaling for well-distributed scores
- **PercentileScoreNormalizer**: Rank-based normalization forcing full 0-1 spread
- **ZScoreNormalizer**: Mean/stddev based normalization (±3σ → 0-1 mapping)
