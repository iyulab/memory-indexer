# Architecture

## Project Structure

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
    ├── Embedding/               # Local (LMSupply), Mock
    ├── Completion/              # Local (LMSupply), Mock
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

## 3-Axis Memory Model

Memory Indexer implements a **3-Axis Memory Model** where each memory has three independent, orthogonal dimensions:

```
Type × Scope × Tier
 ↓      ↓       ↓
What   When   Where
```

### Axis 1: Type (What kind of memory)

Content classification based on cognitive psychology (Tulving's memory classification).

| Type | Description | Example |
|------|-------------|---------|
| `Episodic` | Events with temporal context | "User asked about auth on Dec 8th" |
| `Semantic` | General facts and knowledge | "User prefers dark mode" |
| `Procedural` | How-to patterns and workflows | "Deploy: test → build → staging" |
| `Fact` | Specific verifiable facts | "User's company is Acme Corp" |
| `Reflection` | Synthesized inferences from consolidation | "User prioritizes security over convenience" |

**Type Details:**

- **Episodic**: Contains WHO, WHAT, WHEN, WHERE context. Tied to specific sessions. Example: *"Yesterday we spent 3 hours debugging the auth issue"*
- **Semantic**: Context-free factual knowledge. High reusability across sessions. Example: *"User prefers Python for backend development"*
- **Procedural**: Sequential steps and processes. Action-oriented. Example: *"To deploy: run tests → build → push to staging → smoke test"*
- **Fact**: Atomic discrete data points. Key-value structured. Example: *"API key expires on 2025-01-15"*
- **Reflection**: Generalized patterns from multiple episodes. Example: *"User frequently encounters JWT issues during auth debugging"*

### Axis 2: Scope (Temporal reach)

Defines how far a memory reaches across conversations.

| Scope | Level | Lifetime | API Visibility |
|-------|-------|----------|----------------|
| `Turn` | S3 | ~seconds | Internal (VCM) |
| `Topic` | S2 | ~minutes | Internal (VCM) |
| `Session` | S1 | ~hours | Exposed |
| `User` | S0 | ~forever | Exposed |

**Cognitive Science Basis:**
- Tulving's Episodic/Semantic distinction (context-bound vs context-free)
- Cowan's Short-Term Memory Model (attention focus vs background context)
- Oberauer's Concentric Model (focus of attention → activated LTM)

### Axis 3: Tier (Storage layer)

Storage location based on persistence and lifespan.

| Tier | TTL/Promotion | Storage | Cognitive Basis |
|------|---------------|---------|-----------------|
| `Buffer` (T0) | 60s idle OR 500 tokens OR 3 turns | In-memory | Atkinson-Shiffrin sensory memory |
| `Short` (T1) | 10min OR 2K tokens OR topic change | Memory cache | Baddeley's working memory |
| `Long` (T2) | Session duration | Vector DB (SQLite-vec, Qdrant) | Tulving's episodic memory |
| `Archive` (T3) | Confidence ≥ 0.8 AND Confirms ≥ 3 | Vector DB (persistent) | Tulving's semantic memory |

## Memory Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           USER INPUT                                     │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  BUFFER (T0)                                                            │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Raw conversation staging • Full text • Async processing           │  │
│  │ TTL: 60s idle | 500 tokens | 3 turns (OR logic)                   │  │
│  │ Scope: Turn (S3)                                                  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ BufferPromoter (OR logic)
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  SHORT (T1)                                                             │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Topic-grouped • Summarized chunks • Active context                │  │
│  │ Capacity: 4-7 items • TTL: 10min | 2K tokens | topic change       │  │
│  │ Scope: Topic (S2) → Session (S1)                                  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ ShortTermMemoryOrchestrator (OR logic)
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  LONG (T2)                                                              │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Session experiences • Temporal events • Compressed episodes       │  │
│  │ Storage: Vector DB (SQLite-vec or Qdrant)                         │  │
│  │ Scope: Session (S1)                                               │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ AND logic promotion
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  ARCHIVE (T3)                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Long-term knowledge • Preferences • Identity • Cross-session      │  │
│  │ Promotion: Confidence >= 0.8 AND Confirmations >= 3               │  │
│  │ Scope: User (S0)                                                  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

## 3-Axis Coordination Services

| Service | Interface | Responsibility |
|---------|-----------|----------------|
| **Scope Manager** | `IScopeManager` | Resolves Scope dimension, detects topic changes, tracks session boundaries |
| **Tier Manager** | `ITierManager` | Evaluates tier promotions/demotions, enforces OR/AND logic, manages transitions |
| **Virtual Context Manager** | `IVirtualContextManager` | Orchestrates all three dimensions, coordinates IScopeManager and ITierManager |

## Promotion Logic

**Buffer → Short → Long** (OR Logic):
- Time elapsed >= threshold **OR**
- Token count >= threshold **OR**
- Turn count >= threshold

**Long → Archive** (AND Logic):
- Confidence >= 0.8 **AND**
- Confirmation count >= 3

**Confirmation Sources** (Phase 55):
- Explicit: `memory_confirm` MCP tool call
- Implicit: Duplicate detection during encoding (repeated mention = confirmation)

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
│  VirtualContextManager: coordinates all 3 axes      │
│  MemoryPrimitives: 13 fundamental operations        │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│  3-Axis Memory Layer                                │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐   │
│  │ Buffer  │→│  Short  │→│  Long   │→│ Archive │   │
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

## Core Interfaces

### Tier Interfaces

| Tier | Interface | Implementation |
|------|-----------|----------------|
| Buffer (T0) | `IBuffer` | `BufferService` |
| Short (T1) | `IShortTermMemory` | `ShortTermMemoryService` |
| Long (T2) | `ILongTermStore` | `InMemoryEpisodicStore` |
| Archive (T3) | `IArchiveStore` | `SemanticStoreService` |

### Promotion Services

| Transition | Interface | Implementation | Logic |
|------------|-----------|----------------|-------|
| Buffer → Short | `ISensoryPromoter` | `SensoryPromoterService` | OR |
| Short → Long | `IShortTermMemoryOrchestrator` | `ShortTermMemoryOrchestratorService` | OR |
| Long → Archive | `ILongTermPromoter` | `LongTermPromoterService` | AND |

### Memory Primitives (13 Operations)

| Category | Primitives |
|----------|------------|
| Content | Encode, Update, Split, Merge |
| Lifecycle | Delete, Expire, Lock |
| Classification | Label |
| Retrieval | Retrieve, Summarize |
| Tier | Promote, Demote |
| Validation | Confirm (Phase 53) |

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

    public MemoryType Type { get; set; }      // Episodic, Semantic, Procedural, Fact, Reflection
    public Tier Tier { get; set; }            // Buffer, Short, Long, Archive
    public Scope Scope { get; set; }          // Turn, Topic, Session, User
    public float ImportanceScore { get; set; }
    public int AccessCount { get; set; }
}
```

### MemoryPrimitives Scope Support

```csharp
// Encode with explicit Scope
var memory = await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    Content = "User prefers dark mode",
    Type = MemoryType.Fact,
    Scope = Scope.User,  // Cross-session preference
    Tier = Tier.Archive
});

// Retrieve filtered by Scope
var results = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
{
    Query = "user preferences",
    Scopes = new[] { Scope.User, Scope.Session }
});
```

## Promotion Triggers

### OR Logic (Lower Tiers)

Buffer → Short and Short → Long use OR logic for aggressive cleanup:

```csharp
public enum PromotionTriggerType
{
    None = 0,
    IdleTimeout = 1,      // Buffer: 60s, Short: 10min
    TokenThreshold = 2,   // Buffer: 500, Short: 2K
    TurnThreshold = 3,    // Buffer: 3, Short: 10
    TopicChange = 4,      // Short only
    Manual = 5,
    SessionEnd = 6        // Short only
}
```

### AND Logic (Archive Tier)

Long → Archive uses AND logic for conservative promotion:

```csharp
public class ArchiveStoreOptions
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
| `memory_confirm` | Confirm memory (Phase 53: increments ConfirmCount for Archive eligibility) |

## Dependency Injection

Registration via `AddMemoryIndexer()` extension:

```csharp
// InMemory storage (default)
services.AddMemoryIndexer(options => {
    options.Embedding.Provider = EmbeddingProvider.Local;
});

// Or with SQLite persistent storage
services.AddMemoryIndexer(options => {
    options.Storage.ConnectionString = "memories.db";
    options.Embedding.Provider = EmbeddingProvider.Local;
}).WithSqliteVec();
```

Registers (core services):
- `MemoryService` (orchestration)
- `IMemoryStore` (InMemory by default, or SqliteVec via WithSqliteVec())
- `IEmbeddingService` (based on Embedding.Provider)
- `IScoringService`

Registers (3-Axis architecture):
- `IBuffer` → `BufferService` (T0)
- `IShortTermMemory` → `ShortTermMemoryService` (T1)
- `ILongTermStore` → `InMemoryEpisodicStore` (T2)
- `IArchiveStore` → `ArchiveStoreService` (T3)
- `ISensoryPromoter` → `SensoryPromoterService` (T0→T1)
- `IShortTermMemoryOrchestrator` → `ShortTermMemoryOrchestratorService` (T1→T2)
- `ILongTermPromoter` → `LongTermPromoterService` (T2→T3, Phase 52)

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
      "ShortOrchestrator": {
        "IdleTimeout": "00:10:00",
        "TokenThreshold": 2000,
        "TurnThreshold": 10
      },
      "ArchiveStore": {
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
  - Exact duplicates (≥0.95): Skip storage + implicit confirm (+0.10 confidence boost)
  - High similarity (0.85-0.94): Merge content + implicit confirm (+0.05 confidence boost)
  - Medium similarity (0.75-0.84): Update existing + implicit confirm (+0.02 confidence boost)
  - Low similarity (0.65-0.74): Add with relation
  - **Phase 55**: Duplicate detection = repeated mention = implicit confirmation → enables Archive promotion
- **ImportanceAnalyzer**: Value assessment scoring

### Score Normalization

- **AdaptiveScoreNormalizer**: Auto-selects normalization strategy based on score distribution
  - Narrow spread (< 0.3): Percentile ranking for forced separation
  - High variance (CV > 0.5): Z-score for outlier handling
  - Normal distribution: MinMax linear scaling
- **MinMaxScoreNormalizer**: Linear 0-1 scaling for well-distributed scores
- **PercentileScoreNormalizer**: Rank-based normalization forcing full 0-1 spread
- **ZScoreNormalizer**: Mean/stddev based normalization (±3σ → 0-1 mapping)

## Context Budget API (v0.9.0+)

Token-budget-aware context building that replaces full conversation history with intelligent recall.

### IContextBuilder

```csharp
var request = new ContextRequest(
    UserId: "user123",
    SessionId: "session456",
    Query: "What's my preference?",
    Budget: new ContextBudget(TotalTokens: 2000)
);

var bundle = await contextBuilder.BuildAsync(request, "RecentHeavy");
// bundle.Content contains token-budget-aware context
```

### Built-in Strategies

| Strategy | Recent | Semantic | Episodic | Facts | Best For |
|----------|--------|----------|----------|-------|----------|
| `Balanced` | 30% | 25% | 25% | 20% | General use |
| `RecentHeavy` | 45% | 10% | 35% | 10% | Games, conversations |
| `SemanticHeavy` | 15% | 45% | 15% | 25% | RAG, QA systems |

### Session Isolation

- **Recent turns**: Filtered by `sessionId` (Buffer + ShortTerm)
- **Episodic**: Filtered by `sessionId` (session-scoped experiences)
- **Semantic/Fact**: User-scoped (cross-session knowledge)

## Fact Management (v0.9.1-v0.10.0)

### Intelligent Fact Extraction

AI-based detection distinguishes real user facts from quoted/fictional content:

| Context | Example | Promotion Path |
|---------|---------|----------------|
| Direct statement | "My name is John" | FastTrack → Archive |
| Quoted text | "In the book, he says..." | SessionOnly |
| Hypothetical | "If I were..." | SessionOnly |
| Question | "What is my name?" | Discard |

### Fact Conflict Resolution

Bi-temporal model for conflicting facts:

```csharp
// Temporal queries
var currentFacts = await factStore.GetValidAtAsync(userId, DateTimeOffset.UtcNow);
var historicalFacts = await factStore.GetValidAtAsync(userId, someDate);
```

| Resolution Strategy | When Applied |
|--------------------|--------------|
| `RequireConfirmation` | Identity facts (name, age) |
| `RecencyFirst` | Preferences |
| `TemporalPartition` | Temporal facts (archive old, add new) |
| `ConfidenceFirst` | When confidence diff ≥ 0.2 |

### User Profile Evolution

- **Cross-fact inference**: Derive new facts from existing ones
- **Confidence decay**: Time-based decay with category multipliers
- **Profile snapshots**: Point-in-time versioning with diff comparison
- **GDPR export**: Category filtering, redaction, checksums

## Retention & Observability (v0.11.0)

### Retention Policy

Category-specific retention with GDPR-aligned defaults:

| Category | Default Retention | Notes |
|----------|-------------------|-------|
| Fact | ∞ | Core identity |
| Preference | 365 days | Subject to change |
| Skill | 730 days | Stable over time |
| Goal | 180 days | Time-sensitive |

### Metrics Dashboard

Real-time operational metrics via `IMetricsDashboard`:

```csharp
var health = await dashboard.GetHealthSummaryAsync();
var ops = await dashboard.GetOperationStatisticsAsync(since, until);
var perf = await dashboard.GetPerformanceMetricsAsync();
```

| Metric Category | Includes |
|----------------|----------|
| Health | Component status, alerts, uptime |
| Operations | Success rate, throughput, ops/second |
| Performance | Latency percentiles (P50/P95/P99), cache hit rate |
| Storage | Memory counts, sizes, growth rates |
| Security | PII detections, injection attempts, security score |

## 3-Axis Mental Model

**Understanding the Orthogonality:**

- Think of **Tier** as the "container" (cup, bucket, tank, reservoir)
- Think of **Type** as the "liquid" (water, juice, milk, soda)
- Any liquid can go in any container
- Container size/rules determine promotion (overflow → next container)
- Liquid type determines how it's used (drink, cook, clean)

**Key Insight**: Type and Tier are independent dimensions. Any memory Type can exist at any Tier.

```
Type × Scope × Tier = 5 × 4 × 4 = 80 possible combinations

                │ Episodic │ Semantic │ Procedural │ Fact │ Reflection
────────────────┼──────────┼──────────┼────────────┼──────┼────────────
Buffer (T0)     │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
Short (T1)      │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
Long (T2)       │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
Archive (T3)    │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
```

## Common Misconceptions

### ❌ "Long only contains Episodic type"

**Reality**: Long (T2) stores SESSION MEMORIES. Those sessions can contain ANY type:
- Episodic: "User asked about auth on 2024-12-10"
- Semantic: "User prefers REST over GraphQL" (extracted from session)
- Procedural: "Deployment workflow: test → build → deploy"
- Fact: "Database has 1.2M users" (mentioned in session)

### ❌ "Archive only contains Semantic type"

**Reality**: Archive (T3) stores LONG-TERM CONFIRMED KNOWLEDGE. Any repeatedly confirmed knowledge qualifies:
- Semantic: "User's timezone is UTC-5"
- Procedural: "User's code review process: 1) Format 2) Test 3) Review"
- Fact: "User's GitHub username is @johndoe"

### ❌ "Types determine promotion between tiers"

**Reality**: Promotion is TIER-driven, not TYPE-driven:
- **T0 → T1**: Time (60s) OR Tokens (500) OR Turns (3) [OR logic]
- **T1 → T2**: Time (10min) OR Tokens (2K) OR Topic Change [OR logic]
- **T2 → T3**: Confidence (≥0.8) AND Confirmations (≥3) [AND logic]

All types follow the same promotion rules.

## Design Principles

### Separation of Concerns

| Dimension | Concerns |
|-----------|----------|
| **Tier** | WHERE stored, WHEN promoted, HOW LONG persists |
| **Type** | WHAT kind of content, HOW to interpret, WHY it matters |
| **Scope** | HOW FAR it reaches (turn → user lifetime) |

### Memory Lifecycle Example

**"User prefers dark mode"** journey:

1. **T0 Buffer** (Semantic): Raw statement captured
2. **T1 Short** (Semantic): Topic-grouped "User Preferences"
3. **T2 Long** (Semantic): Session extract "dark mode preference"
4. **T3 Archive** (Semantic): Confirmed after 3 mentions across sessions

**Type unchanged, Tier evolved**: Semantic → Semantic → Semantic → Semantic
