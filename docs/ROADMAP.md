# Roadmap

## Phase 1: Foundation ✅

**Status**: Complete

- [x] Solution structure (7 projects + 4 test projects)
- [x] Core domain models (MemoryUnit, Session, EntityTriple)
- [x] InMemory storage with vector search
- [x] Basic MCP tools (store, recall, get, list, update, delete)
- [x] Standalone console application
- [x] SDK package structure
- [x] Unit tests

## Phase 2: Intelligence ✅

**Status**: Complete

- [x] SQLite-vec persistent storage
- [x] BGE-M3 embeddings via Ollama
- [x] Hybrid search (dense + sparse with BM25)
- [x] Topic segmentation
- [x] Importance scoring (recency + relevance + importance)
- [x] Duplicate detection and merging
- [x] RRF (Reciprocal Rank Fusion) algorithm

## Phase 3: Advanced Features ✅

**Status**: Complete

- [x] Hierarchical summarization (ExtractiveSummarizer with TextRank)
- [x] LLMLingua-2 compression (token/sentence pruning)
- [x] Knowledge graph entities (EntityExtractor, EntityGraph)
- [x] Self-editing memory (MemGPT-style VCM architecture)
- [x] Context window optimization (ContextWindowOptimizer)
- [x] Qdrant integration (QdrantMemoryStore)

## Phase 4: Production ✅

**Status**: Complete

- [x] PII detection (RegexPiiDetector with configurable patterns)
- [x] Prompt injection defense (PromptInjectionDetector)
- [x] Multi-tenant isolation (TenantContext, RBAC authorization)
- [x] RAGAS evaluation pipeline (DefaultRetrievalEvaluator)
- [x] OpenTelemetry observability (Phase 4.5)
- [x] NuGet package configuration (Phase 4.6)

## Phase 5: Intelligence Layer ✅

**Status**: Complete

- [x] LLMLingua-style compression strategies
- [x] Cross-encoder reranking (LocalReranker with LMSupply.Reranker)
- [x] Heuristic memory classification
- [x] 3-Tier VCM architecture (Working/Archival/Core memory)
- [x] Memory primitives (insert, replace, search)

## Phase 6: Advanced Retrieval ✅

**Status**: Complete

- [x] HyDE (Hypothetical Document Embeddings) query expansion
- [x] Parent-Child chunk retrieval pattern
- [x] LoCoMo evaluation framework (MRR, NDCG, Answer Coverage)
- [x] ONNX Runtime compatibility fix for .NET 10

## Phase 7: Production Deployment (Planned)

**Status**: Planned

- [ ] Kubernetes deployment patterns
- [ ] Health check endpoints
- [ ] Performance monitoring dashboards
- [ ] Load testing and benchmarks
- [ ] Documentation and samples

## Phase 8: Temporal Knowledge Graph ✅

**Status**: Complete

- [x] Temporal entity store (ITemporalEntityStore, InMemoryTemporalEntityStore)
- [x] Semantic contradiction detection (SemanticContradictionDetector)
- [x] Contradiction resolution strategies (DefaultContradictionResolver)
- [x] Graph-based retrieval with multi-hop traversal (IGraphRetriever)
- [x] Entity relationship management with temporal validity

## Phase 9: Memory Consolidation ✅

**Status**: Complete

- [x] SLEEP paradigm implementation (Stabilize, Link, Extract, Evaluate, Prune)
- [x] Memory consolidator interface (IMemoryConsolidator)
- [x] Sleep-based consolidation strategy (SleepBasedConsolidator)
- [x] Importance-weighted memory pruning
- [x] Relationship strengthening during consolidation
- [x] Semantic clustering and insight extraction

## Phase 10: Intelligent Memory Operations ✅

**Status**: Complete

- [x] Semantic operation decider (ADD/UPDATE/DELETE/NOOP/MERGE/REPLACE)
- [x] Embedding-based duplicate and contradiction detection
- [x] Importance analysis for value assessment
- [x] Topic extraction and memory type detection
- [x] Threshold-based summarization triggers
- [x] Token budget, session end, and importance-based triggers
- [x] Strategy recommendations (Extractive, Compression, Hybrid, Reflection, Archive)

## Phase 11: Session-aware Summarization ✅

**Status**: Complete

- [x] SummarizationOrchestrator integrating triggers with summarization services
- [x] Rolling Summary Manager for periodic session summarization
- [x] Turn-based, time-based, and token-threshold triggers
- [x] Incremental updates using CoK-style merging
- [x] Strategy-based summarization (Extractive, Compression, Hybrid, Reflection, Archive)

## Phase 12: Entity Resolution Enhancement ✅

**Status**: Complete

- [x] Coreference resolution (ICoreferenceResolver, CoreferenceResolver)
- [x] Pronoun resolution (he/she/it/they → entity)
- [x] Possessive and reflexive pronoun support
- [x] Text expansion (replace pronouns with entity names)
- [x] Coreference chains for entity mention tracking
- [x] Gender and number agreement validation

## Phase 13: Hybrid Scoring & Samples ✅

**Status**: Complete

- [x] Keyword matching boost (query word matching ratio)
- [x] Content-type boosting (CONFIRMED +0.3, RULED OUT +0.1)
- [x] Hybrid score integration in RecallAsync
- [x] TwentyQuestionsGame sample (memory-only context demonstration)
- [x] MemoryChatApp sample with web frontend
- [x] 26 new unit tests for scoring service

## 4-Tier Memory Architecture (Core Design)

### Tier Overview

```
┌─────────────────────────────────────────────────────────────────┐
│  Tier 1: Recently (Buffer)                                      │
│  ├── Raw conversation, full text                                │
│  ├── Async processing staging area                              │
│  └── TTL: 60s idle OR 500 tokens OR 3 turns                     │
├─────────────────────────────────────────────────────────────────┤
│  Tier 2: Working (Active Context)                               │
│  ├── Summarized, topic-grouped                                  │
│  ├── Changes on topic switch                                    │
│  └── TTL: 10min OR 2K tokens OR 10 turns OR topic_change        │
├─────────────────────────────────────────────────────────────────┤
│  Tier 3: Session (Archive)                                      │
│  ├── Session summary, key facts                                 │
│  ├── Explicit new session OR 30min idle                         │
│  └── Compressed representation                                  │
├─────────────────────────────────────────────────────────────────┤
│  Tier 4: User (Profile Dictionary)                              │
│  ├── Structured facts: preferences, identity, relationships     │
│  ├── Key-value with versioning                                  │
│  └── Promotion: importance > 0.7 AND frequency >= 2             │
└─────────────────────────────────────────────────────────────────┘
```

### Multi-Signal Promotion Triggers

| Transition | Signal | Threshold | Logic |
|------------|--------|-----------|-------|
| Recently → Working | Time | 60s idle | OR |
| | Tokens | 500 accumulated | OR |
| | Turns | 3 conversation turns | OR |
| Working → Session | Time | 10min since topic | OR |
| | Tokens | 2000 in working | OR |
| | Turns | 10 turns same topic | OR |
| | Topic | Change detected | OR |
| Session → User | Importance | > 0.7 score | AND |
| | Frequency | Mentioned 2+ times | AND |
| | Type | fact/preference/identity | AND |

**Design Principle**:
- **Lower promotions (Recently→Working→Session)**: OR logic — aggressive buffer cleanup
- **Upper promotion (Session→User)**: AND logic — conservative, only important facts

### Tier Data Models

```csharp
// Tier 1: Recently - Raw buffer
record RecentlyMemory(
    string Content,           // Full conversation text
    DateTime Timestamp,
    int TokenCount,
    int TurnIndex
);

// Tier 2: Working - Summarized active context
record WorkingMemory(
    string Summary,           // Compressed representation
    string Topic,             // Current topic label
    List<string> KeyPoints,   // Extracted key points
    DateTime TopicStarted,
    int AccumulatedTokens
);

// Tier 3: Session - Archived session
record SessionMemory(
    string SessionId,
    string Summary,           // Session-level summary
    List<EntityTriple> Facts, // Extracted facts
    DateTime StartTime,
    DateTime EndTime,
    Dictionary<string, float> TopicWeights
);

// Tier 4: User - Profile dictionary
record UserMemory(
    Dictionary<string, UserFact> Facts,  // Structured facts
    DateTime LastUpdated
);

record UserFact(
    string Value,
    float Confidence,
    int MentionCount,
    DateTime FirstMentioned,
    DateTime LastMentioned,
    List<string> SourceSessions   // Provenance tracking
);
```

### Async Processing Pipeline

```
[User Input]
     │
     ▼
[Recently Buffer] ←─── Immediate storage (sync, <10ms)
     │
     ├─── Trigger check (async)
     │         │
     │         ▼
     │    [Promotion Worker]
     │         │
     │         ├─ Extract entities (LLM async)
     │         ├─ Generate summary (LLM async)
     │         └─ Calculate importance (embedding async)
     │
     ▼
[Working Memory] ←─── Topic-grouped summaries
     │
     ├─── Topic change detection (async)
     │
     ▼
[Session Archive] ←─── Session summaries + facts
     │
     ├─── Importance filter (async)
     │
     ▼
[User Profile] ←─── Structured dictionary
```

### Conflict Resolution (User Tier)

```yaml
conflict_strategy:
  same_key_update:
    rule: "Latest wins with version history"
    example:
      T1: user.coffee_preference = "loves coffee" (v1)
      T2: user.coffee_preference = "quit coffee" (v2, current)
    retention: "Keep last 3 versions for context"

  contradicting_facts:
    rule: "Higher confidence wins, flag for review if close"
    threshold: 0.1 confidence difference
    example:
      fact1: "vegetarian" (confidence: 0.9)
      fact2: "ate steak yesterday" (confidence: 0.8)
      action: "Flag contradiction, keep both with notes"
```

---

## Phase 14: 4-Tier Memory Architecture ✅

**Status**: Complete

**Goal**: Implement the 4-tier memory architecture with intelligent compression and multi-signal promotion triggers

### 14.1 Recently Tier Implementation ✅

- [x] **Buffer Management**
  - [x] RecentlyMemoryBuffer with thread-safe storage
  - [x] Token counting integration (~4 chars/token)
  - [x] Turn tracking per user
  - [x] Async promotion trigger monitoring
  - [x] IRecentlyBuffer interface + RecentlyBufferService

- [x] **Promotion Triggers**
  - [x] Time-based trigger (60s idle detection)
  - [x] Token-based trigger (500 token threshold)
  - [x] Turn-based trigger (3 turn threshold)
  - [x] Multi-signal OR evaluation

### 14.2 Working Tier Implementation ✅

- [x] **Buffer Promotion (Recently → Working)**
  - [x] IBufferPromoter interface
  - [x] BufferPromoterService with topic segmentation
  - [x] Topic grouping using TopicSegmenter
  - [x] MemoryUnit creation with embeddings
  - [x] Eviction handling for capacity management

- [x] **Topic Management**
  - [x] Topic segmentation via embedding similarity
  - [x] Topic label extraction
  - [x] Importance scoring based on message count/length

### 14.3 Session Tier Implementation ✅

- [x] **Session Lifecycle**
  - [x] IWorkingMemoryOrchestrator interface
  - [x] WorkingMemoryOrchestratorService
  - [x] Multi-signal triggers (IdleTimeout, TokenThreshold, TurnThreshold, TopicChange)
  - [x] Per-user state tracking

- [x] **Archival Pipeline**
  - [x] Extractive summarization for session archives
  - [x] Importance-weighted memory selection
  - [x] Session tier demotion handling
  - [x] Summary embedding generation

### 14.4 User Tier Implementation ✅

- [x] **Profile Dictionary**
  - [x] IUserProfile interface
  - [x] UserProfileService with in-memory storage
  - [x] UserProfileEntry with categories and confidence
  - [x] Semantic search with embeddings

- [x] **Promotion Logic (AND Logic)**
  - [x] Confirmation count requirement (>= 3)
  - [x] Confidence threshold (>= 0.8)
  - [x] Evidence tracking per confirmation
  - [x] Category-based organization (Fact, Preference, Skill, Interest, etc.)

### Test Coverage
- 28 tests for RecentlyBuffer (Phase 14.1)
- 15 tests for BufferPromoter (Phase 14.2)
- 19 tests for WorkingMemoryOrchestrator (Phase 14.3)
- 23 tests for UserProfile (Phase 14.4)
- **Total: 85+ new tests for 4-tier architecture**

### Success Criteria
- ✅ Complete 4-tier implementation (Recently→Working→Session→User)
- ✅ Multi-signal promotion with OR logic (lower tiers)
- ✅ AND logic promotion for User tier (conservative)
- ✅ Per-user state tracking and isolation
- ✅ Comprehensive test coverage (504+ total tests)

---

## Phase 15: Smart Tiered Retrieval ✅

**Status**: Complete

**Goal**: Return compressed meaning instead of full text, with adaptive context assembly based on query type

### 15.1 Query Intent Classification ✅

- [x] **IQueryIntentClassifier interface**
  - [x] Factual intent (prioritize User tier)
  - [x] Contextual intent (prioritize Working tier)
  - [x] Temporal intent (prioritize Session tier with time filters)
  - [x] Relational intent (prioritize Graph traversal)
  - [x] General intent (balanced retrieval)

- [x] **LocalQueryIntentClassifier implementation**
  - [x] Heuristic-based pattern matching
  - [x] Temporal reference extraction
  - [x] Entity reference extraction
  - [x] Keyword extraction with stopword filtering
  - [x] Secondary intent detection for ambiguous queries
  - [x] 35 tests covering all intent types

### 15.2 Tiered Retrieval Strategy ✅

- [x] **ITieredRetrievalStrategy interface**
  - [x] Query intent-based tier priority routing
  - [x] Token budget estimation per tier
  - [x] Tier-specific boost factors

- [x] **TieredMemoryRetriever implementation**
  - [x] H-MEM inspired hierarchical routing
  - [x] Intent-to-tier weight mapping (Factual→User, Contextual→Working, etc.)
  - [x] Parallel retrieval from prioritized tiers
  - [x] Graph context retrieval for relational queries
  - [x] Recency boost calculation
  - [x] Cosine similarity ranking

### 15.3 Adaptive Context Assembly ✅

- [x] **IAdaptiveContextAssembler interface**
  - [x] AFM-inspired fidelity levels (Full, Compressed, Placeholder)
  - [x] Token budget allocation
  - [x] Multiple output formats (Markdown, PlainText, XML, JSON)

- [x] **AdaptiveContextAssembler implementation**
  - [x] Full fidelity: Complete content for high-priority items
  - [x] Compressed fidelity: First sentence + summary indicator
  - [x] Placeholder fidelity: Minimal reference (type, hint, age)
  - [x] Budget-aware truncation
  - [x] Tier headers and metadata options
  - [x] Graph context integration
  - [x] 20 tests for context assembly

### 15.4 Token Budget Allocation ✅

- [x] **Intent-based budget weights**
  - [x] Factual: Working 15%, Session 25%, User 50%, Graph 10%
  - [x] Contextual: Working 50%, Session 30%, User 10%, Graph 10%
  - [x] Temporal: Working 15%, Session 50%, User 25%, Graph 10%
  - [x] Relational: Working 10%, Session 20%, User 30%, Graph 40%
  - [x] General: Balanced 30%/30%/30%/10%

- [x] **Fidelity budget allocation**
  - [x] Full fidelity: 60% of total budget (default)
  - [x] Compressed fidelity: 30% of total budget
  - [x] Placeholder fidelity: 10% of total budget

### Research Basis

Based on H-MEM (Hierarchical Memory) and AFM (Adaptive Focus Memory) research:
- Query-aware tier routing (H-MEM index-based selection)
- Adaptive fidelity levels (AFM FULL/COMPRESSED/PLACEHOLDER)
- Token budget optimization

### Test Coverage
- 35 tests for LocalQueryIntentClassifier
- 20 tests for AdaptiveContextAssembler
- **Total: 55 new tests for Phase 15**

### Success Criteria
- ✅ Intent classification with > 80% accuracy
- ✅ Adaptive fidelity with configurable budgets
- ✅ Token-aware context assembly
- ✅ Comprehensive test coverage (559 total tests)

---

## Phase 16: Graph-based Memory Network ✅

**Status**: Complete

**Goal**: Implement Mem0g-style graph memory for relationship-aware retrieval

### 16.1 Memory-to-Graph Integration ✅

- [x] **IMemoryGraphService interface**
  - [x] MemoryGraphNode linking memories to entity graph
  - [x] Entity-to-memory bidirectional mapping
  - [x] FindRelatedMemoriesAsync (multi-hop traversal)
  - [x] ExtractSubgraphAsync (focused retrieval)

- [x] **MemoryGraphService implementation**
  - [x] In-memory graph storage with efficient indexing
  - [x] BFS-based multi-hop traversal
  - [x] Relationship strength calculation (distance, shared entities)
  - [x] Subgraph extraction with configurable options

### 16.2 Community Detection ✅

- [x] **ICommunityDetector interface**
  - [x] DetectCommunitiesAsync for topic clustering
  - [x] AssignToCommunityAsync for new memories
  - [x] GetCommunitySummaryAsync for community metadata

- [x] **LabelPropagationCommunityDetector implementation**
  - [x] Label Propagation Algorithm (Raghavan et al., 2007)
  - [x] O(m) time complexity with graph edges
  - [x] Weighted edges using relationship confidence
  - [x] Modularity calculation for quality assessment
  - [x] Convergence detection with configurable iterations

### 16.3 Importance Propagation ✅

- [x] **IImportancePropagator interface**
  - [x] ComputeImportanceAsync for global ranking
  - [x] GetEntityImportanceAsync for single entity
  - [x] GetTopEntitiesAsync for ranked list

- [x] **PageRankImportancePropagator implementation**
  - [x] PageRank algorithm with damping factor 0.85
  - [x] Weighted edges using relationship confidence
  - [x] Convergence detection with configurable threshold
  - [x] Result caching per user for efficiency

### 16.4 Graph-enhanced Query Expansion ✅

- [x] **IGraphQueryExpander interface**
  - [x] ExpandQueryAsync for query enrichment
  - [x] ExtractQueryEntitiesAsync for entity detection
  - [x] GenerateSubQueriesAsync for decomposition

- [x] **GraphQueryExpander implementation**
  - [x] Quoted entity extraction ("John Smith")
  - [x] Capitalized word entity detection
  - [x] Multi-word entity matching (New York City)
  - [x] High-importance entity matching
  - [x] Graph traversal for related entities
  - [x] Community context integration
  - [x] Sub-query generation (facts, relationships)

### Test Coverage
- 4 tests for MemoryGraphService
- 9 tests for LabelPropagationCommunityDetector
- 8 tests for PageRankImportancePropagator
- 8 tests for GraphQueryExpander
- **Total: 29 new tests for Phase 16**

### Success Criteria
- ✅ Multi-hop traversal implemented
- ✅ Community detection with modularity scoring
- ✅ PageRank-style importance propagation
- ✅ Query expansion via graph traversal
- ✅ Comprehensive test coverage (588 total tests)

---

## Phase 17: Self-Directed Memory Management ✅

**Status**: Complete

**Goal**: MemGPT-inspired autonomous memory management with LLM-driven decisions

### 17.1 Autonomous Memory Manager ✅

- [x] **IAutonomousMemoryManager interface**
  - [x] Heartbeat-based operation scheduling
  - [x] Autonomous page-in/page-out decisions
  - [x] Context-aware memory retrieval
  - [x] Access pattern tracking and statistics

- [x] **AutonomousMemoryManager implementation**
  - [x] State tracking (last heartbeat, pending operations)
  - [x] Memory access recording (read/write/update)
  - [x] Optimization suggestions based on access patterns
  - [x] Memory operation request handling

### 17.2 Memory Self-Correction ✅

- [x] **IMemorySelfCorrector interface**
  - [x] AnalyzeMemoriesAsync for health assessment
  - [x] DetectContradictionsAsync for semantic conflicts
  - [x] IdentifyOutdatedMemoriesAsync for staleness detection
  - [x] ApplyCorrectionsAsync for automated fixes

- [x] **MemorySelfCorrector implementation**
  - [x] Contradiction detection (factual, temporal, preference, identity)
  - [x] Resolution strategies (KeepNewest, KeepOlder, KeepHigherConfidence, Merge, FlagForReview)
  - [x] Evidence gap tracking for incomplete memories
  - [x] Confidence score updates with time decay
  - [x] Correction history tracking

### 17.3 Reflection Engine ✅

- [x] **IReflectionEngine interface**
  - [x] ShouldReflectAsync (importance threshold evaluation)
  - [x] ReflectAsync (insight generation from memories)
  - [x] GenerateInsightsAsync (pattern extraction)
  - [x] DiscoverLinksAsync (relationship discovery)
  - [x] SummarizeActivityAsync (session summaries)
  - [x] SynthesizeQuestionsAsync (knowledge gaps)

- [x] **ReflectionEngine implementation**
  - [x] Importance-based reflection triggers
  - [x] Time-since-last-reflection checks
  - [x] Memory clustering for insight extraction
  - [x] Temporal link discovery
  - [x] Entity-based link detection
  - [x] Topic extraction for activity summaries

### 17.4 Enhanced Agent Memory Tools ✅

- [x] **AutonomousMemoryTools MCP integration**
  - [x] heartbeat tool for scheduling
  - [x] optimize_memory tool for consolidation
  - [x] page_in / page_out tools for memory management
  - [x] reflect tool for insight generation
  - [x] analyze_health tool for memory assessment
  - [x] self_correct tool for automated fixes

### Test Coverage
- 21 tests for AutonomousMemoryManager
- 16 tests for MemorySelfCorrector
- 18 tests for ReflectionEngine
- **Total: 55 new tests for Phase 17**

### Success Criteria
- ✅ Autonomous operation framework implemented
- ✅ Self-correction with contradiction resolution
- ✅ Reflection engine with insight generation
- ✅ MCP tools for agent integration
- ✅ Comprehensive test coverage (638 total tests)

---

## Phase 18: Production & Ecosystem (In Progress)

**Status**: Phase 18.1 Completed ✅

**Goal**: Production-ready deployment and ecosystem integration

### Phase 18.1: Health & Observability ✅

**Status**: Completed
**Date**: January 2026

#### Implemented Features

- [x] **Comprehensive Health Checks**
  - 4-Tier memory health monitoring (Recently, Working, Session, User)
  - Infrastructure health (Vector DB, Embedding Service)
  - Kubernetes-compatible endpoints (/health/ready, /health/live, /health/startup)
  - Tag-based health check filtering (/health/tier/{tier})

- [x] **ASP.NET Core Health Integration**
  - HealthCheckResponseWriter for JSON responses
  - HealthCheckExtensions for DI registration
  - Microsoft.Extensions.Diagnostics.HealthChecks integration

- [x] **Test Coverage**
  - 16 comprehensive unit tests for all health checks
  - Mocked dependencies for isolated testing
  - Coverage: RecentlyBuffer, WorkingMemory, VectorDb, EmbeddingService health checks

#### Technical Details

- **Package Added**: `Microsoft.AspNetCore.Http.Abstractions 2.2.0`
- **Health Thresholds**:
  - Recently Buffer: 2000 tokens (warning), 5000 tokens (critical)
  - Working Memory: 85% utilization (warning), 95% utilization (critical)
  - Vector DB: 200ms query latency (warning), 500ms (critical)
  - Embedding Service: 1000ms latency (warning), 2000ms (critical)

### Phase 18.2: Performance & REST API (In Progress)

**Goal**: Production performance validation and non-MCP client support

- [x] **Load Testing & Benchmarks** ✅
  - BenchmarkDotNet integration for performance testing
  - 10 core operation benchmarks (Store, Recall, GetAll, Update, Delete)
  - Workflow benchmarks for 4-tier architecture integration
  - Storage layer benchmarks for direct vector operations
  - Memory diagnostics with allocation tracking
  - Sequential vs parallel operation benchmarks
  - Mixed memory type workflow testing

- [x] **Memory Usage Optimization** (Phase 18.2.3) 🔄
  - [x] Memory profiling and analysis (docs/MEMORY_OPTIMIZATION.md)
  - [x] Quick Win: Null empty collections (Topics, Entities, Metadata)
  - [x] Identified 6 optimization areas with priorities
  - [x] BenchmarkDotNet integration for baseline measurement
  - [ ] Resource usage monitoring and memory pressure detection
  - [ ] Lazy embedding loading
  - [ ] Embedding quantization (float16/int8)

- [x] **REST API Wrapper** ✅
  - HTTP REST endpoints for non-MCP clients (6 endpoints: POST, GET, PUT, DELETE)
  - OpenAPI/Swagger documentation with Swashbuckle
  - Full CRUD operations: Store, Search, GetAll, Get, Update, Delete
  - Compatible with MemoryService architecture

---

## Research References

### Key Papers & Projects

| Reference | Key Insight | Applied To |
|-----------|-------------|------------|
| **MemGPT** | OS-inspired 2-tier paging, self-directed editing | Phase 17 |
| **Mem0** | Extraction + Update phases, 91% latency reduction | Phase 14 |
| **Mem0g** | Graph-based memory with entity extraction, community detection | Phase 16 |
| **H-MEM** | Multi-level semantic abstraction, index routing | Phase 15 |
| **LangChain** | ConversationSummaryBuffer pattern | Phase 14 |
| **Recursive Summarization** | summary += new_content iteratively | Phase 14 |
| **PageRank** | Graph-based importance propagation, damping factor | Phase 16 |
| **Label Propagation** | O(m) community detection algorithm | Phase 16 |

### Design Principles (from Research)

1. **Tier count matters less than compression quality**
   - 3-tier + strong summarization > 5-tier + weak summarization
   - Focus on HOW to compress, not how many levels

2. **Extraction + Update pattern is key**
   - Don't just store raw text
   - Extract meaning → Update state → Store compressed

3. **Query-aware retrieval**
   - Return summaries by default
   - Expand to details only when needed
   - Adaptive based on query intent

4. **Self-directed management**
   - Let LLM decide what to remember
   - Autonomous consolidation and pruning
   - Proactive relevance maintenance

## Success Metrics

| Metric | Target | Current |
|--------|--------|---------|
| Retrieval Latency (p95) | < 100ms | ✅ Achieved |
| Context Recall | > 80% | ✅ Achieved |
| Faithfulness | > 85% | ✅ Achieved |
| Token Reduction | > 80% | ✅ Achieved |
| Test Coverage | > 80% | ✅ 654 tests |

## Technical Notes

### .NET 10 Compatibility
- ONNX Runtime has native binary incompatibility with .NET 10
- ONNX-dependent tests skipped via `SKIP_ONNX_TESTS` compile constant
- GpuStack embedding tests remain active (HTTP-based)
- Monitor ONNX Runtime updates for .NET 10 support

---

*Last Updated: 2026-01-06*
