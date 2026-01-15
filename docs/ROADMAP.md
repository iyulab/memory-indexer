# Roadmap

Development roadmap for Memory Indexer.

## Released

### v0.6.0 - Production Readiness (Current)

**Observability:**
- [x] OpenTelemetry distributed tracing via Activity Source
- [x] Instrumented services (InstrumentedMemoryPrimitives, InstrumentedVCM)
- [x] Semantic conventions for all operation types

**Backup/Restore:**
- [x] IMemoryExporter interface with export/import operations
- [x] JsonMemoryExporter implementation with checksum verification
- [x] Conflict resolution strategies (Skip, Replace, KeepNewer, KeepHigherConfidence, Fail)
- [x] BackupRestoreTools MCP (ExportMemories, ImportMemories, GetBackupStats)
- [x] Incremental backup support via Since/Until filters

**Resource Management:**
- [x] IResourceLimitEnforcer interface with enforcement logic
- [x] IUsageTracker interface with thread-safe in-memory implementation
- [x] Per-user resource limits (max memories, max storage bytes)
- [x] Multi-tenant limit configuration (tenant-specific limits)
- [x] OpenTelemetry metrics for resource usage telemetry
- [x] ResourceManagementTools MCP (GetUsage, GetLimits, CanStore, CanStoreBatch, GetTenantUsage, GetGlobalSummary, RefreshUsage)
- [x] Enforcement integration in MCP MemoryTools (pre-store checks)

**Provider Architecture:**
- [x] Removed built-in Ollama/OpenAI embedding implementations
- [x] Removed built-in Ollama/OpenAI completion implementations
- [x] LocalEmbeddingService uses LMSupply.Embedder (ONNX-based embeddings)
- [x] LocalTextCompletionService uses LMSupply.Generator (ONNX-based text generation)
- [x] MockTextCompletionService for development/testing
- [x] Interface-based design: IEmbeddingService, ITextCompletionService for external implementations

### v0.5.0 - Intelligence Integration

Focus: Expose existing SDK intelligence features via MCP tools and production polish.

**MCP Tool Enhancements:**
- [x] Conflict detection/resolution MCP tools (DetectContradiction, ResolveContradiction, AutoResolve, GetResolutionStrategy)
- [x] Adaptive retrieval MCP tools (ClassifyQueryIntent, AdaptiveRecall, TieredRecall, GetRetrievalRecommendation)
- [x] Graph traversal MCP tools (DetectCommunities, ComputeImportance, GetTopEntities, FindRelatedMemories, ExtractSubgraph)

**Integration & Testing:**
- [x] Unit tests for Graph Traversal MCP tools (14 tests)
- [x] End-to-end integration tests (IntelligenceIntegrationTests: 17 tests)
- [x] TwentyQuestionsGame sample: Beta reasoning chain improvements

**Production Polish:**
- [x] OpenTelemetry metrics for intelligence operations
- [x] Configuration validation (IConfigurationValidator, 21 validation rules)
- [x] Documentation for advanced intelligence features (docs/INTELLIGENCE.md)

**Efficiency Improvements:**
- [x] Session-level recall caching (SHA256 cache keys, configurable TTL)
- [x] Recall pattern telemetry (RecallPatternAnalyzer)
- [x] Token budget awareness hooks (ITokenBudgetMonitor)

### v0.4.0 - Cognitive Architecture

**3-Axis Memory Model** implementing Atkinson-Shiffrin, Baddeley, and Tulving's memory models.

**3-Axis Model (Type × Scope × Tier):**
- **Type**: Episodic, Semantic, Procedural, Fact, Reflection
- **Scope**: Turn, Topic, Session, User (temporal reach)
- **Tier**: Buffer, Short, Long, Archive (storage layer)

**Tier Promotion Pipeline:**
- Buffer → Short: Idle timeout, token threshold, or turn count
- Short → Long: Automatic when capacity exceeded (7±2 rule)
- Long → Archive: Confidence ≥ 0.8 AND Confirms ≥ 3

**MCP Tools:**
- `memory_store` / `memory_recall` / `memory_update` / `memory_delete`
- `memory_confirm` - Explicit confirmation for Archive promotion
- `memory_get_all` - Filtered retrieval by tier/type

**Performance:**
- Store: ~2.2μs (460K ops/sec)
- Recall: ~1.5μs (670K ops/sec)
- Vector search: ~812ns (1.2M ops/sec)

### v0.3.0 - Foundation

Initial release with basic memory primitives and vector storage.

---

## Planned

### v0.7.0 - Evaluation Framework & Package Architecture

**Standardized Evaluation (MemoryBench):**

*Core KPIs:*
- [x] Context Compression Ratio (CCR): `recalled_tokens / full_history_tokens`
- [x] Recall@K Efficiency: Precision of top-K retrieval
- [x] Tier Promotion Latency: Buffer → Short → Long transition time
- [x] Information Retention Score: Long-term recall accuracy

*NIAH (Needle In A Haystack) Test:*
- [x] Haystack generator (synthetic or external source)
- [x] Needle insertion at configurable positions (25%, 50%, 75%)
- [x] `store` → `recall` validation pipeline
- [x] CCR verification (target: <1% of full context)
- [x] Reference: [gkamradt/LLMTest_NeedleInAHaystack](https://github.com/gkamradt/LLMTest_NeedleInAHaystack)

*Cognitive Scenarios:*
- [x] False Memory Test: Conflicting info update detection
- [x] Cross-Session Retention: Archive tier persistence validation

**Package Structure:**
```
MemoryIndexer              # Core interfaces and abstractions
MemoryIndexer.Sdk          # InMemory, SQLite, LMSupply (default embedding)
MemoryIndexer.Redis        # Redis storage backend
MemoryIndexer.PostgreSQL   # PostgreSQL with pgvector
MemoryIndexer.Qdrant       # Qdrant vector database
MemoryIndexer.Stack        # Meta-package bundling all packages
```

**Distributed Storage:**
- Redis cluster support with connection pooling
- PostgreSQL with pgvector for enterprise deployments
- Separate packages by external dependency

**Multi-tenancy:**
- Full tenant isolation
- Resource quotas per tenant
- Usage metering and reporting

### v0.8.0 - Production Readiness

**Health & Observability (Phase 18.1):**
- [x] ASP.NET Core Health Checks integration
- [x] Memory tier health checks (Buffer, ShortTerm, LongTerm, Archive)
- [x] Infrastructure health checks (VectorDb, Embedding)
- [x] Kubernetes probe endpoints (/health/live, /health/ready, /health/startup)
- [x] JSON response writer with detailed diagnostics
- [x] Tag-based health check filtering
- [x] Health check documentation (docs/HEALTH.md)

**Performance & Benchmarks (Phase 18.2):**
- [x] BenchmarkDotNet performance suite
- [x] Tier promotion benchmarks (Buffer → Short → Long → Archive)
- [x] Concurrency/load benchmarks (10, 50, 100 concurrent ops)
- [x] Local benchmark script (benchmarks/run_bench.ps1)
- [x] Benchmark documentation (docs/BENCHMARKS.md)

**Planned (Phase 18.3+):**
- [ ] REST API wrapper with OpenAPI

### v0.9.0 - Context Budget API (Complete)

**Token-Budget-Aware Context Building:**

*Problem Statement:*
Current recall is item-count based, not token-aware. Sequential conversations (games, multi-turn tasks) suffer from insufficient recent context while semantic recall works well for RAG/QA scenarios.

*Core Features:*
- [x] `IContextStrategy` interface for pluggable context building
- [x] `ContextBudget` configuration (RecentTokens, SemanticTokens, EpisodicTokens, TotalBudget)
- [x] Token counting utilities (ITokenCounter with ApproximateTokenCounter)

*Flexible Recall APIs:*
- [x] `GetRecentTurns(maxTokens)` - Sequential recent conversation up to N tokens
- [x] `GetSemanticContext(query, maxTokens)` - Query-relevant memories within budget
- [x] `GetSessionContext(sessionId, maxTokens)` - Session-scoped episodic recall
- [x] `GetUserFactsAsync(userId, maxTokens)` - User-specific semantic facts

*Built-in Strategies:*
- [x] `BalancedStrategy` - 30% recent, 25% semantic, 25% episodic, 20% facts
- [x] `RecentHeavyStrategy` - 45% recent, 10% semantic, 35% episodic, 10% facts (games, conversations)
- [x] `SemanticHeavyStrategy` - 15% recent, 45% semantic, 15% episodic, 25% facts (RAG, QA)
- [x] `CustomStrategy` - Consumer-defined allocation

*Session Isolation:*
- [x] Episodic memories isolated to session scope
- [x] Semantic/Fact memories shared across sessions (user scope)
- [x] Clear separation of session context vs user knowledge

*MCP Tool Extensions:*
- [x] `context_build` - Token-budget-aware context building
- [x] `get_recent_conversation` - Get last N tokens of conversation
- [x] `get_session_context` - Session-scoped episodic recall
- [x] `get_user_facts` - User-scoped persistent facts
- [x] `get_context_strategies` - List available strategies

---

### v0.9.1 - Intelligent Fact Extraction (Complete)

**AI-Based Fact Detection and Fast-Track Promotion:**

*Problem Statement:*
High-confidence user facts (e.g., "My name is John") should be immediately promoted to user-level storage, but quoted/fictional content (e.g., "In the novel, 'My name is Lincoln'") must be filtered out. This requires AI-based context detection.

*Core Features:*
- [x] `IFactExtractor` interface for context-aware fact extraction
- [x] `LlmFactExtractor` with confidence scoring and context detection
- [x] `MockFactExtractor` for testing
- [x] `PromotionPath` enum (FastTrack, Standard, SessionOnly, Discard)

*Context Detection:*
- [x] Direct statements ("My name is John") → FastTrack
- [x] Quoted text ("In the book, he says...") → SessionOnly
- [x] Hypothetical ("If I were...") → SessionOnly
- [x] Narrative/RolePlay → SessionOnly
- [x] Questions → Discard

*Fact Categories:*
- [x] Identity (name, age, occupation)
- [x] Preference (likes, favorites)
- [x] Relationship (family, friends)
- [x] Location (address, workplace)
- [x] Professional (job, company)
- [x] Health (allergies, conditions)

*Fast-Track Promotion Pipeline:*
- [x] `IFastTrackPromoter` interface
- [x] `FastTrackPromoterService` implementation
- [x] Category mapping (FactCategory ↔ SemanticStoreCategory)
- [x] Direct Buffer → Archive path for confidence ≥ 0.9
- [x] MCP tools: `extract_facts`, `get_user_profile`, `get_fact_categories`, `get_promotion_paths`

---

### v0.9.2 - Fact Conflict Resolution (Complete)

**Stricter Criteria for Conflicting Facts:**

*Problem Statement:*
When new facts conflict with existing ones, stricter resolution criteria are needed. Simple recency-based resolution is insufficient for identity facts (e.g., name changes should require explicit confirmation).

*Conflict Detection:*
- [x] Enhanced semantic similarity for fact comparison
- [x] Subject-Predicate-Object (SPO) triple matching
- [x] Confidence differential threshold (+0.2 for auto-resolution)
- [x] Category-specific resolution rules (10 categories)

*Resolution Strategies:*
- [x] Identity facts: Require explicit confirmation for changes
- [x] Preference facts: Allow update with moderate confidence
- [x] Temporal facts: Archive old, add new with timestamps
- [x] Contradictions: Mark for review if confidence difference < 0.2
- [x] RecencyFirst, ConfidenceFirst, TemporalPartition, KeepBoth, RequireConfirmation

*Bi-Temporal Model:*
- [x] `ValidFrom` timestamp (when fact became true)
- [x] `ValidTo` timestamp (when fact stopped being true)
- [x] Temporal queries (`GetValidAtAsync` for point-in-time queries)
- [x] Fact history chain via `SupersedesKey`
- [x] `WasValidAt()` and `CreateSupersedingVersion()` methods

*MCP Tools:*
- [x] `validate_fact` - Check for conflicts before storing
- [x] `archive_and_update_fact` - Update with version archival
- [x] `get_fact_history` - Retrieve temporal fact chain
- [x] `get_facts_valid_at` - Query facts valid at specific date
- [x] `get_category_rule` - Get resolution rule for category
- [x] `get_all_category_rules` - List all category rules

---

### v0.10.0 - User Profile Evolution (Future)

**Long-Term User Knowledge Management:**

*User Fact Graph:*
- [ ] Entity-relationship model for user facts
- [ ] Fact clustering by category
- [ ] Cross-fact inference (e.g., "lives in Seoul" + "works at Samsung" → "commutes in Seoul")

*Profile Evolution:*
- [ ] Change detection and tracking
- [ ] Confidence decay over time
- [ ] Re-confirmation prompts for stale facts
- [ ] Profile snapshot/versioning

*Advanced Queries:*
- [ ] Temporal range queries
- [ ] Category-filtered retrieval
- [ ] Confidence-weighted results
- [ ] Profile diff (what changed since last session)

*Privacy & Compliance:*
- [ ] Fact deletion with cascade
- [ ] Export user profile (GDPR)
- [ ] Fact sensitivity classification
- [ ] Retention policies by category

### v1.0.0+ - Advanced Benchmarks & Administration (Future)

**Extended Evaluation:**
- [ ] RULER integration (multi-needle retrieval, reasoning)
- [ ] LongBench subset (QA, summarization tasks)
- [ ] InfiniteBench (100K+ token extreme tests)
- [ ] Automated scorecard generation
- [ ] Fact extraction accuracy benchmarks

**Administration:**
- [ ] Memory analytics dashboard
- [ ] Usage reporting APIs
- [ ] GDPR compliance tools, data retention policies
- [ ] User profile management UI

---

## Philosophy

> "The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."

We implement **forgetting as a feature** - memory decay, importance-based filtering, and tier promotion ensure context windows stay optimized while preserving what matters.

---

*Last updated: 2026-01-15 (v0.9.2-complete)*
