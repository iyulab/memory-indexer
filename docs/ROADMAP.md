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

## Phase 15: Smart Tiered Retrieval (Planned)

**Status**: Planned

**Goal**: Return compressed meaning instead of full text, with adaptive context assembly based on query type

### Retrieval Strategy

- [ ] **Query Intent Classification**
  - [ ] Factual recall ("what did I say about...")
  - [ ] Contextual recall ("continue our conversation about...")
  - [ ] Relational recall ("what's related to...")
  - [ ] Temporal recall ("last week we discussed...")

- [ ] **Adaptive Context Assembly**
  - [ ] Summary-first retrieval (compressed understanding)
  - [ ] Detail-on-demand expansion (drill down when needed)
  - [ ] Relevance-weighted inclusion (most relevant = more detail)
  - [ ] Token budget allocation (distribute across tiers)

- [ ] **Tiered Response Structure**
  ```
  User Level:  "User enjoys art, particularly Japanese animation"
  Session Level: "Discussing Ghibli films and their artistic style"
  Recent Level: "Specific details about Spirited Away color palette"
  ```

- [ ] **Context Window Optimization**
  - [ ] Dynamic tier selection based on available tokens
  - [ ] Priority-based truncation (keep summaries, trim details)
  - [ ] Query-aware expansion (expand relevant portions only)

### Implementation Notes
- Integrate with existing VirtualContextManager
- Extend ContextWindowOptimizer for tiered optimization
- Add retrieval mode parameter to RecallAsync

### Success Criteria
- Context size reduction: > 70% vs full-text retrieval
- Answer quality preservation: > 90%
- Retrieval latency: < 150ms

---

## Phase 16: Graph-based Memory Network (Planned)

**Status**: Planned

**Goal**: Implement Mem0g-style graph memory for relationship-aware retrieval

### Graph Architecture

- [ ] **Memory Graph Schema**
  - [ ] Entity nodes (people, places, concepts)
  - [ ] Memory nodes (conversation chunks)
  - [ ] Relationship edges (typed connections)
  - [ ] Temporal edges (sequence, causality)

- [ ] **Graph Operations**
  - [ ] Multi-hop traversal for context gathering
  - [ ] Relationship-based similarity search
  - [ ] Subgraph extraction for focused retrieval
  - [ ] Community detection for topic clustering

- [ ] **Integration with Existing Systems**
  - [ ] Extend InMemoryGraphRetriever
  - [ ] Add Qdrant graph layer (payload relationships)
  - [ ] Connect EntityExtractor → Graph population
  - [ ] Link CoreferenceResolver → Entity unification

- [ ] **Query Expansion via Graph**
  - [ ] Related entity inclusion
  - [ ] Path-based context gathering
  - [ ] Importance propagation (PageRank-style)

### Success Criteria
- Multi-hop accuracy: > 80%
- Relationship recall: > 85%
- Graph query latency: < 100ms

---

## Phase 17: Self-Directed Memory Management (Planned)

**Status**: Planned

**Goal**: MemGPT-inspired autonomous memory management with LLM-driven decisions

### Autonomous Operations

- [ ] **Memory Paging** (MemGPT-style)
  - [ ] Main context (working memory, limited tokens)
  - [ ] External context (archival storage, unlimited)
  - [ ] Automatic page-in/page-out decisions
  - [ ] LLM-triggered memory retrieval

- [ ] **Self-Editing Capabilities**
  - [ ] Memory importance re-evaluation
  - [ ] Contradiction self-correction
  - [ ] Redundancy elimination
  - [ ] Proactive consolidation triggers

- [ ] **Reflection Mechanism**
  - [ ] Periodic memory review
  - [ ] Pattern extraction from recent memories
  - [ ] Insight generation and storage
  - [ ] Memory quality self-assessment

- [ ] **Agent Memory Interface**
  - [ ] Tool-based memory access (store, retrieve, edit)
  - [ ] Memory state visibility for agents
  - [ ] Cross-agent memory sharing (optional)

### Implementation Notes
- Build on existing SelfEditingMemoryTools
- Extend VirtualContextManager for paging
- Add LLM integration for decision making

### Success Criteria
- Autonomous operation accuracy: > 90%
- Memory freshness maintenance: automatic
- Context relevance: > 95% (human eval)

---

## Phase 18: Production & Ecosystem (Planned)

**Status**: Planned

**Goal**: Production-ready deployment and ecosystem integration

### Deployment

- [ ] Kubernetes deployment patterns
- [ ] Health check endpoints
- [ ] Performance monitoring dashboards
- [ ] Load testing and benchmarks
- [ ] Memory usage optimization

### Ecosystem

- [ ] LangChain integration adapter
- [ ] Semantic Kernel memory provider
- [ ] OpenAI Assistants API compatibility
- [ ] REST API wrapper (non-MCP clients)

### Documentation

- [ ] Architecture deep-dive guide
- [ ] Configuration cookbook
- [ ] Performance tuning guide
- [ ] Migration guides (version upgrades)

---

## Research References

### Key Papers & Projects

| Reference | Key Insight | Applied To |
|-----------|-------------|------------|
| **MemGPT** | OS-inspired 2-tier paging, self-directed editing | Phase 17 |
| **Mem0** | Extraction + Update phases, 91% latency reduction | Phase 14 |
| **H-MEM** | Multi-level semantic abstraction, index routing | Phase 15 |
| **LangChain** | ConversationSummaryBuffer pattern | Phase 14 |
| **Recursive Summarization** | summary += new_content iteratively | Phase 14 |

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
| Test Coverage | > 80% | ✅ 337+ tests |

## Technical Notes

### .NET 10 Compatibility
- ONNX Runtime has native binary incompatibility with .NET 10
- ONNX-dependent tests skipped via `SKIP_ONNX_TESTS` compile constant
- GpuStack embedding tests remain active (HTTP-based)
- Monitor ONNX Runtime updates for .NET 10 support

---

*Last Updated: 2026-01-05*
