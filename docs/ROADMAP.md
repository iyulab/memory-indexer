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

### v0.8.0+ - Advanced Benchmarks (Future)

**Extended Evaluation:**
- [ ] RULER integration (multi-needle retrieval, reasoning)
- [ ] LongBench subset (QA, summarization tasks)
- [ ] InfiniteBench (100K+ token extreme tests)
- [ ] Automated scorecard generation

**Administration:**
- Memory analytics dashboard
- Usage reporting APIs
- GDPR compliance tools, data retention policies

---

## Philosophy

> "The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."

We implement **forgetting as a feature** - memory decay, importance-based filtering, and tier promotion ensure context windows stay optimized while preserving what matters.

---

*Last updated: 2026-01-10 (v0.7.0-preview)*
