# Roadmap

Development roadmap for Memory Indexer.

## Released

### v0.4.0 - Cognitive Architecture (Current)

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

### v0.5.0 - Intelligence Integration (In Progress)

Focus: Expose existing SDK intelligence features via MCP tools and production polish.

**MCP Tool Enhancements:**
- [x] Conflict detection/resolution MCP tools (DetectContradiction, ResolveContradiction, AutoResolve, GetResolutionStrategy)
- [x] Adaptive retrieval MCP tools (ClassifyQueryIntent, AdaptiveRecall, TieredRecall, GetRetrievalRecommendation)
- [x] Graph traversal MCP tools (DetectCommunities, ComputeImportance, GetTopEntities, FindRelatedMemories, ExtractSubgraph)

**Integration & Testing:**
- [x] Unit tests for Graph Traversal MCP tools (14 tests)
- [ ] End-to-end integration tests with real LLM providers
- [x] TwentyQuestionsGame sample: Beta reasoning chain improvements (done in v0.4.0)

**Production Polish:**
- [ ] OpenTelemetry metrics for intelligence operations
- [ ] Configuration validation and better defaults
- [ ] Documentation for advanced intelligence features

### v0.6.0 - Production Readiness

- **Distributed Storage**: Redis, PostgreSQL backends
- **Multi-tenancy**: Isolated user contexts with resource limits
- **Backup/Restore**: Memory state persistence and migration
- **Observability**: Full OpenTelemetry tracing integration

---

## Philosophy

> "The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."

We implement **forgetting as a feature** - memory decay, importance-based filtering, and tier promotion ensure context windows stay optimized while preserving what matters.

---

*Last updated: 2026-01-09 (v0.5.0-preview.1)*
