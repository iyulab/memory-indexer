# Roadmap

Development roadmap for Memory Indexer.

## Released

### v0.5.0 - Intelligence Integration (Current)

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

## In Progress

### v0.6.0 - Production Readiness

**Observability (Complete):**
- [x] OpenTelemetry distributed tracing via Activity Source
- [x] Instrumented services (InstrumentedMemoryPrimitives, InstrumentedVCM)
- [x] Semantic conventions for all operation types

**Backup/Restore (Complete):**
- [x] IMemoryExporter interface with export/import operations
- [x] JsonMemoryExporter implementation with checksum verification
- [x] Conflict resolution strategies (Skip, Replace, KeepNewer, KeepHigherConfidence, Fail)
- [x] BackupRestoreTools MCP (ExportMemories, ImportMemories, GetBackupStats)
- [x] Incremental backup support via Since/Until filters

**Resource Management (Complete):**
- [x] IResourceLimitEnforcer interface with enforcement logic
- [x] IUsageTracker interface with thread-safe in-memory implementation
- [x] Per-user resource limits (max memories, max storage bytes)
- [x] Multi-tenant limit configuration (tenant-specific limits)
- [x] OpenTelemetry metrics for resource usage telemetry
- [x] ResourceManagementTools MCP (GetUsage, GetLimits, CanStore, CanStoreBatch, GetTenantUsage, GetGlobalSummary, RefreshUsage)
- [x] Enforcement integration in MCP MemoryTools (pre-store checks)

**Remaining:**
- [ ] Distributed Storage: Redis, PostgreSQL backends
- [ ] Multi-tenancy: Full tenant isolation (resource limits now available)

---

## Planned

### v0.7.0 - Enterprise Features

- **Advanced Storage**: Redis cluster, PostgreSQL with pgvector
- **Multi-tenancy**: Tenant isolation, resource quotas, usage metering
- **Administration**: Memory analytics dashboard, usage reporting
- **Compliance**: GDPR tools, data retention policies

---

## Philosophy

> "The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."

We implement **forgetting as a feature** - memory decay, importance-based filtering, and tier promotion ensure context windows stay optimized while preserving what matters.

---

*Last updated: 2026-01-09 (v0.6.0-preview.3)*
