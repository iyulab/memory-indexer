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
- [x] Cross-encoder reranking (LocalReranker with LocalAI.Reranker)
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

## Phase 8: Advanced Algorithms (Future)

**Status**: Future

- [ ] Memory consolidation during "sleep"
- [ ] Contradictory memory resolution
- [ ] Temporal reasoning improvements
- [ ] Multi-hop reasoning optimization

## Success Metrics

| Metric | Target | Current |
|--------|--------|---------|
| Retrieval Latency (p95) | < 100ms | ✅ Achieved |
| Context Recall | > 80% | ✅ Achieved |
| Faithfulness | > 85% | ✅ Achieved |
| Token Reduction | > 80% | ✅ Achieved |
| Test Coverage | > 80% | ✅ 196+ tests |

## Technical Notes

### .NET 10 Compatibility
- ONNX Runtime has native binary incompatibility with .NET 10
- ONNX-dependent tests skipped via `SKIP_ONNX_TESTS` compile constant
- GpuStack embedding tests remain active (HTTP-based)
- Monitor ONNX Runtime updates for .NET 10 support

---

*Last Updated: 2025-12-15*
