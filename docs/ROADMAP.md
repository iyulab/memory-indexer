# Roadmap

## Phase 1: Foundation ✅

**Status**: Complete

- [x] Solution structure (7 projects + 3 test projects)
- [x] Core domain models (MemoryUnit, Session, EntityTriple)
- [x] InMemory storage with vector search
- [x] Basic MCP tools (store, recall, get, list, update, delete)
- [x] Standalone console application
- [x] SDK package structure
- [x] Unit tests (13 passing)

## Phase 2: Intelligence

**Status**: Planned

- [ ] SQLite-vec persistent storage
- [ ] BGE-M3 embeddings via Ollama
- [ ] Hybrid search (dense + sparse)
- [ ] Topic segmentation
- [ ] Importance scoring (recency + relevance + importance)
- [ ] Duplicate detection and merging

## Phase 3: Advanced Features

**Status**: Planned

- [ ] Hierarchical summarization
- [ ] LLMLingua-2 compression
- [ ] Knowledge graph entities
- [ ] Self-editing memory (MemGPT pattern)
- [ ] Context window optimization
- [ ] Qdrant integration

## Phase 4: Production

**Status**: Planned

- [ ] PII detection (Microsoft Presidio)
- [ ] Prompt injection defense
- [ ] Multi-tenant isolation
- [ ] RAGAS evaluation pipeline
- [ ] OpenTelemetry observability
- [ ] NuGet package publishing

## Success Metrics

| Metric | Target |
|--------|--------|
| Retrieval Latency (p95) | < 100ms |
| Context Recall | > 80% |
| Faithfulness | > 85% |
| Token Reduction | > 80% |
| Test Coverage | > 80% |
