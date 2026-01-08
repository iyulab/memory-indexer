# Roadmap

Development roadmap for Memory Indexer.

## Released

### v0.4.0 - Cognitive Architecture (Current)

4-Tier Cognitive Memory Architecture implementing Atkinson-Shiffrin and Tulving's memory models.

**Key Features:**
- **Buffer (T0)**: Sensory memory store with TTL-based expiration
- **Short-Term (T1)**: Baddeley's working memory (7±2 capacity limit)
- **Long-Term (T2)**: Tulving's episodic memory for session events
- **Archive (T3)**: Tulving's semantic memory with confirmation-based promotion

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

### v0.5.0 - Intelligence Enhancement

- **Adaptive Retrieval**: Context-aware recall strategies
- **Conflict Resolution**: Automated contradiction handling
- **Graph Memory**: Entity relationship tracking (Mem0-inspired)
- **Multi-modal**: Support for structured data types

### v0.6.0 - Production Readiness

- **Distributed Storage**: Redis, PostgreSQL backends
- **Observability**: OpenTelemetry integration
- **Multi-tenancy**: Isolated user contexts
- **Backup/Restore**: Memory state persistence

---

## Philosophy

> "The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."

We implement **forgetting as a feature** - memory decay, importance-based filtering, and tier promotion ensure context windows stay optimized while preserving what matters.

---

*Last updated: 2026-01-09 (v0.4.0)*
