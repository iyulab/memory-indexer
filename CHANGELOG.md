# Changelog

All notable changes to Memory Indexer are documented here.

## [v0.6.0-preview.4] - 2026-01-09

### Provider Architecture Simplification

This preview removes built-in Ollama/OpenAI implementations in favor of interface-based design. Memory Indexer now focuses on memory management while delegating LLM/embedding concerns to external implementations.

#### Breaking Changes
- **Removed**: `OllamaEmbeddingService`, `OpenAIEmbeddingService` - Use external implementations or `LocalEmbeddingService` (LMSupply)
- **Removed**: `OllamaCompletionService`, `OpenAICompletionService` - Register your own `ITextCompletionService`
- **Removed**: `GpuStackEmbeddingTests` - Integration tests for removed services
- **Default Changed**: `CompletionOptions.Provider` now defaults to `Mock` instead of `Ollama`

#### Built-in Providers
- **Embedding**: `LocalEmbeddingService` (LMSupply), `MockEmbeddingService`
- **Completion**: `MockTextCompletionService` (new)

#### Interface-Based Design
For production use with external LLMs, register your own implementation before calling `AddMemoryIndexer()`:
```csharp
services.AddSingleton<IEmbeddingService, YourEmbeddingService>();
services.AddSingleton<ITextCompletionService, YourCompletionService>();
services.AddMemoryIndexer();
```

#### Future Package Structure (v0.7.0)
- `MemoryIndexer` - Core interfaces
- `MemoryIndexer.Sdk` - InMemory, SQLite, LMSupply
- `MemoryIndexer.Redis/PostgreSQL/Qdrant` - Storage backends
- `MemoryIndexer.Stack` - Full bundle

**Tests**: All 1150 tests passing (237 core + 913 SDK, -4 removed integration tests)

---

## [v0.6.0-preview.3] - 2026-01-09

### Resource Management

This preview adds comprehensive resource limit enforcement and usage tracking for multi-tenant deployments.

#### IResourceLimitEnforcer Interface (`IResourceLimitEnforcer.cs`)
- **Enforcement Methods**: `CanStoreAsync`, `CanStoreBatchAsync`
- **Query Methods**: `GetLimits`, `GetUsageAsync`
- **EnforcementResult**: IsAllowed, DenialReason, ExceededLimit, CurrentUsage, Limits
- **ResourceLimits**: MaxMemories, MaxStorageBytes, EnforcementEnabled, WarningThresholdPercent, Source
- **LimitType**: MemoryCount, StorageSize enum for exceeded limit identification

#### IUsageTracker Interface (`IUsageTracker.cs`)
- **Recording**: `RecordStore`, `RecordDelete`, `RecordTierPromotion`
- **Queries**: `GetUsage`, `GetTenantUsage`, `GetGlobalSummary`, `GetTrackedUsers`
- **Maintenance**: `RefreshFromStoreAsync`, `ClearUser`
- **ResourceUsage**: UserId, TenantId, MemoryCount, StorageSizeBytes, ByTier, ByType, CalculatedAt

#### InMemoryUsageTracker Implementation (`InMemoryUsageTracker.cs`)
- Thread-safe tracking with `ConcurrentDictionary` and `Interlocked` operations
- Per-user breakdown by Tier and MemoryType
- Tenant-level aggregation with user breakdown
- Global summary with top users by count and storage
- Automatic refresh from memory store

#### ResourceLimitEnforcer Implementation (`ResourceLimitEnforcer.cs`)
- Configuration-based limits via `ResourceLimitOptions`
- Tenant-specific limit overrides via `ITenantContext`
- OpenTelemetry telemetry for enforcement events and warnings
- Warning threshold detection (default 80%)

#### Configuration Options (`ResourceLimitOptions`)
- `MaxMemoriesPerUser`: Default 100,000
- `MaxStorageBytesPerUser`: Default 1GB
- `EnforcementEnabled`: Toggle for enforcement
- `WarningThresholdPercent`: Alert threshold (80%)

#### ResourceManagementTools MCP (`ResourceManagementTools.cs`)
- `GetUsage`: Get current resource usage statistics
- `GetLimits`: Get applicable resource limits
- `CanStore`: Check if store operation allowed
- `CanStoreBatch`: Check if batch operation allowed
- `GetTenantUsage`: Get tenant-level aggregation
- `GetGlobalSummary`: Get global usage statistics
- `RefreshUsage`: Force refresh from store

#### MCP MemoryTools Integration
- Pre-store enforcement check in `StoreMemory`
- Usage recording on successful store/delete
- Denial response with clear reason messaging

**Tests**: 43 new tests (ResourceLimitEnforcerTests: 19, InMemoryUsageTrackerTests: 24)
**Total Tests**: All 1154 tests passing (237 core + 917 SDK)

---

## [v0.6.0-preview.2] - 2026-01-09

### Memory Export/Import (Backup/Restore)

This preview adds complete backup and restore capabilities via JSON export/import.

#### IMemoryExporter Interface (`IMemoryExporter.cs`)
- **Export Operations**: `ExportAsync`, `ExportToStreamAsync`
- **Import Operations**: `ImportAsync`, `ImportFromStreamAsync`
- **ExportOptions**: UserId, SessionId, Since/Until filters, Tiers, Types, IncludeEmbeddings, IncludeMetadata
- **ImportOptions**: ConflictResolution, PreserveIds, ValidateChecksum, DryRun
- **ImportConflictResolution**: Skip, Replace, KeepNewer, KeepHigherConfidence, Fail

#### JsonMemoryExporter Implementation (`JsonMemoryExporter.cs`)
- JSON-based serialization with camelCase naming
- SHA256 checksum for data integrity verification
- Comprehensive conflict resolution strategies
- Activity tracing integration via `MemoryIndexerTelemetry`
- Statistics tracking: ByTier, ByType, UniqueUsers, EmbeddingsIncluded

#### BackupRestoreTools MCP (`BackupRestoreTools.cs`)
- `ExportMemories`: Export memories to JSON with filtering options
- `ImportMemories`: Import memories from JSON with conflict resolution
- `GetBackupStats`: Get export statistics without performing full export

#### InMemoryMemoryStore Enhancement
- Preserves explicitly set `CreatedAt`/`UpdatedAt` timestamps (only sets defaults when values are `default`)
- Enables accurate timestamp-based filtering for incremental backups

**Tests**: 11 new tests covering export, import, filtering, conflict resolution, streaming

---

## [v0.6.0-preview.1] - 2026-01-09

### OpenTelemetry Distributed Tracing

This preview adds comprehensive distributed tracing across all memory operations.

#### Activity Source Integration (`MemoryIndexerTelemetry`)
- **Source Name**: `MemoryIndexer` with version tracking
- **Store Operations**: `memory_indexer.store`, `memory_indexer.store_batch`
- **Recall Operations**: `memory_indexer.recall`, `memory_indexer.recall_advanced`
- **Update Operations**: `memory_indexer.update`, `memory_indexer.delete`
- **VCM Operations**: `memory_indexer.vcm_store`, `memory_indexer.vcm_recall`, `memory_indexer.vcm_sync`
- **Intelligence Operations**: `memory_indexer.classify`, `memory_indexer.summarize`, `memory_indexer.rerank`

#### Activity Tags (OpenTelemetry Semantic Conventions)
- `user.id`, `session.id`: User and session context
- `memory.type`, `memory.tier`, `memory.scope`: 3-axis model dimensions
- `memory.count`, `result.count`: Operation metrics
- `db.operation`, `db.system`: Database conventions
- Error tracking: `otel.status_code`, `exception.type`, `exception.message`

#### Instrumented Services
- `InstrumentedMemoryPrimitives`: Wraps IMemoryPrimitives with Activity spans
- `InstrumentedVCM`: Wraps IVirtualContextManager with Activity spans
- All intelligence services emit Activities for tracing

**Tests**: All 1111 tests passing (237 core + 874 SDK)

---

## [v0.5.0] - 2026-01-09

### Intelligence Integration Release

This release completes the v0.5.0 Intelligence Integration phase, exposing existing SDK intelligence features via MCP tools for LLM consumption with comprehensive documentation and testing.

#### Documentation: Advanced Intelligence Features (`docs/INTELLIGENCE.md`)
- **Conflict Resolution**: Contradiction detection and resolution strategies
  - `DetectContradiction`, `ResolveContradiction`, `AutoResolveContradiction`, `GetResolutionStrategy`
  - Configurable thresholds and semantic/rule-based hybrid detection
- **Adaptive Retrieval**: Intent-based query routing and tiered memory access
  - `ClassifyQueryIntent`, `AdaptiveRecall`, `TieredRecall`, `GetRetrievalRecommendation`
  - Query intent types: Factual, Contextual, Temporal, Relational, General
- **Graph Traversal**: Entity-based memory navigation and community detection
  - `DetectCommunities`, `ComputeImportance`, `GetTopEntities`, `FindRelatedMemories`, `ExtractSubgraph`
  - PageRank importance propagation, Label propagation community detection
- **Efficiency Features**: Token budget monitoring, recall pattern analysis, configuration validation
- **OpenTelemetry Metrics**: Complete observability integration

#### Integration Tests (`IntelligenceIntegrationTests.cs`)
- 17 tests covering complete intelligence pipeline
- Configuration validation (valid, invalid, Baddeley warnings)
- Token budget monitoring (sessions, thresholds, recommendations)
- Recall pattern analysis (duplicates, recommendations)
- Query intent classification (Factual, Contextual, Temporal)
- Conflict resolution workflow (detection, strategy, resolution)
- Full pipeline integration test

#### DI Registration Fix
- Registered `IQueryIntentClassifier` → `LocalQueryIntentClassifier` in `ServiceCollectionExtensions`
- Enables query intent classification via dependency injection

**Tests**: All 1100 tests passing (237 core + 863 SDK)

---

## [v0.5.0-preview.2] - 2026-01-09

### Production Polish Phase

This preview adds configuration validation, token budget awareness, and complete OTel intelligence metrics.

#### Token Budget Awareness Hooks (`ITokenBudgetMonitor`)
- Session-level token tracking with configurable warning thresholds
- Events: `OnBudgetWarning`, `OnBudgetExceeded`, `OnSessionEnded`
- Token estimation (~4 chars/token approximation)
- Recommendation system: Continue → ReduceScope → Compress → Conserve → Stop
- Operation breakdown tracking for analysis
- Global stats aggregation across sessions
- 16 tests covering all functionality

#### Configuration Validation (`IConfigurationValidator`)
- Validates all `MemoryIndexerOptions` sections at startup
- Returns structured errors and warnings
- Validates thresholds (0-1 ranges), positive values, required fields
- Cross-field constraints (MaxLimit >= DefaultLimit)
- Cognitive model warnings (Baddeley's 7±2 capacity)
- Type distribution sum validation
- API key warnings for cloud providers
- 21 tests covering comprehensive validation scenarios

#### Complete OpenTelemetry Intelligence Metrics
- **Counters**: Classifications, Summarizations, Deduplications, Conflict detections, Entity extractions, Rerankings, Tier promotions, Token budget warnings/exceeded, Graph queries
- **Histograms**: Classification, Summarization, Deduplication, Reranking, Graph query latency, Token budget usage ratio
- Helper methods for all intelligence operations with appropriate tags

**Tests**: All 1085 tests passing (237 core + 848 SDK)

---

## [v0.5.0-preview.1] - 2026-01-09

### Intelligence Integration Preview

This preview release exposes existing SDK intelligence features via MCP tools for LLM consumption.

#### New MCP Tools

**Graph Traversal Tools** (`GraphTraversalTools.cs`):
- `DetectCommunities` - Detect memory clusters using label propagation algorithm
- `GetCommunityMemories` - Get all memories in a specific community
- `GetCommunitySummary` - Get topic labels and key entities for a community
- `ComputeImportance` - Run PageRank to compute entity importance scores
- `GetEntityImportance` - Get importance score for a specific entity
- `GetTopEntities` - Get ranked list of most important entities
- `FindRelatedMemories` - Find memories related through shared entities
- `ExtractSubgraph` - Extract focused subgraph around specific memories

**Conflict Resolution Tools** (`ConflictResolutionTools.cs`):
- `DetectContradiction` - Detect if new content contradicts existing memories
- `ResolveContradiction` - Resolve contradiction between new content and existing memory
- `AutoResolveContradiction` - Automatically detect and resolve contradictions
- `GetResolutionStrategy` - Get recommendation for handling contradiction types

**Adaptive Retrieval Tools** (`AdaptiveRetrievalTools.cs`):
- `ClassifyQueryIntent` - Classify query intent for optimal retrieval strategy
- `AdaptiveRecall` - Smart retrieval with auto-selected strategy based on intent
- `TieredRecall` - Retrieve from specific tiers with custom priority order
- `GetRetrievalRecommendation` - Get recommendations for information type retrieval

#### Efficiency Improvements

**Session-level Recall Caching** (`OptimizedRecallService`):
- Query result caching with SHA256 cache keys for collision resistance
- TTL controlled via `LatencyOptions.QueryCacheTtlMinutes` (default: 10 min)
- `RecallCacheStatistics` for monitoring: hits, misses, duplicates, hit ratio
- Eliminates redundant embedding generation and vector search operations

**Recall Pattern Telemetry** (`RecallPatternAnalyzer`):
- Duplicate query detection with per-user tracking
- Rapid-fire recall pattern detection (configurable threshold)
- Per-user and global statistics (`RecallPatternStatistics`)
- Alert generation for problematic patterns (`RecallPatternAlert`)
- Optimization recommendations: caching, batching, query consolidation

**OpenTelemetry Metrics** (`MemoryIndexerTelemetry`):
- `memory_indexer.query_cache_hits` - Query result cache hits
- `memory_indexer.duplicate_recalls` - Duplicate recall queries detected
- `memory_indexer.rapid_fire_recalls` - Rapid-fire patterns detected
- Helper methods: `RecordQueryCacheHit`, `RecordDuplicateRecall`, `RecordRapidFireRecall`

#### Technical Details
- All tools use existing SDK intelligence services (no new implementations)
- GraphTraversalTools uses `IMemoryGraphService`, `IImportancePropagator`, and `ICommunityDetector`
- ConflictResolutionTools uses `IContradictionDetector` and `IContradictionResolver`
- AdaptiveRetrievalTools uses `IQueryIntentClassifier` and `TieredMemoryRetriever`
- Tests: All 1048 tests passing (216 core + 832 SDK)

#### Lessons Learned (TwentyQuestionsGame Evaluation)

**Validated Strengths:**
- Recall latency ~5ms (sufficient for real-time conversation)
- Core memory similarity 0.95 (critical information preserved)
- Cognitive compliance (7±2 rule) working correctly
- 21-minute session with zero errors

**Identified Improvements (added to v0.5.0 roadmap):**
- Session-level recall caching needed (LLM made 3x identical queries per turn)
- Recall pattern telemetry for detecting inefficient usage
- Token budget awareness hooks for resource monitoring

---

## [v0.4.0] - 2026-01-09

### Cognitive Architecture Completion

This release completes the 3-Axis Cognitive Memory Architecture (Type × Scope × Tier) with full tier promotion pipeline and cognitive compliance validation.

#### Phase 60: Test Code Warning Fixes
- **Fixed**: CS0219, CS8602, CS8625, xUnit2002, xUnit1026, xUnit2013 warnings in tests
- **Scope**: 5 test files across MemoryIndexer.Tests and MemoryIndexer.Sdk.Tests
- **Pattern**: Null checks for Metadata, proper xUnit assertion usage
- **Tests**: All 1015 tests passing (216 core + 799 SDK)

#### Phase 59: Benchmarks and Documentation
- **Added**: `benchmarks/run_bench.ps1` PowerShell script for automated benchmarks
- **Added**: `docs/BENCHMARKS.md` with detailed performance measurements
- **Updated**: README.md simplified with core architecture and quick start
- **Performance**: Store ~2.2μs, Recall ~1.5μs, Vector search ~812ns

#### Phase 58: Null Reference Warning Fixes
- **Fixed**: 19 CS8602/CS8603/CS8604 nullable warnings in source code
- **Scope**: MemoryPrimitivesService, MemoryService, AdvancedMemoryTools, MemoryTools
- **Pattern**: `??=` initialization for Metadata, `?? []` coalescing for collections
- **Files**: Core services and MCP tools

#### Phase 56: Per-User Cognitive Compliance Fix
- **Fixed**: Cognitive compliance check now evaluates per-user instead of globally
- **Root Cause**: Compliance summed Short tier across all users; enforcement was per-user
- **Impact**: Baddeley's 7±2 model correctly applies to each user (mind) independently
- **Files**: `samples/TwentyQuestionsGame/Benchmark/BenchmarkResult.cs`, `GameRunner.cs`

#### Phase 55: Deduplication→Confirmation Integration
- **Feature**: Duplicate detection now auto-confirms memories
- **Mechanism**: Repeated mention of facts triggers implicit confirmation
- **Files**: `MemoryPrimitivesService.cs`, `IDeduplicationService.cs`

#### Phase 53: Memory Confirmation Primitive
- **Feature**: New `memory_confirm` MCP tool for explicit confirmation
- **Purpose**: Enables Archive tier promotion eligibility (AND logic)
- **API**: `ConfirmAsync(ConfirmRequest request)`
- **Files**: `IMemoryPrimitives.cs`, `MemoryPrimitivesService.cs`, `MemoryTools.cs`

#### Phase 52: Long→Archive Promotion Pipeline
- **Feature**: Complete tier promotion from Long (T2) to Archive (T3)
- **Logic**: AND requirements - Confidence ≥ 0.8 AND ConfirmCount ≥ 3
- **Service**: `ILongTermPromoter` / `LongTermPromoterService`
- **Files**: `MemoryPromotionBackgroundService.cs`

#### Phase 51: Working Memory Capacity Enforcement
- **Feature**: Baddeley's 7±2 capacity limit for Short tier
- **Behavior**: Auto-promotes oldest items when capacity exceeded
- **Config**: `WorkingMemoryOptions.Capacity` (default: 9)
- **Files**: `MemoryPrimitivesService.cs`, `MemoryIndexerOptions.cs`

#### Phase 50: Cognitive Compliance Metrics Revision
- **Change**: Revised compliance checks aligned with cognitive science
- **Metrics**: WorkingMemory(7±2), HealthyTierFlow
- **Files**: `samples/TwentyQuestionsGame/Game/GameRunner.cs`

#### Phase 49: Cognitive-Aware Tier Selection
- **Feature**: Content-based tier assignment in game sample
- **Logic**: Game rules → Short, Q&A history → Long
- **Files**: `samples/TwentyQuestionsGame/ToolCall/ToolCallExecutor.cs`

#### Phase 48: Duplicate Question Bug Fix
- **Fixed**: Beta agent asking semantically duplicate questions
- **Solution**: Enhanced reasoning chain in BetaSystemPrompt
- **Files**: `samples/TwentyQuestionsGame/Prompts/BetaSystemPrompt.md`

### Architecture Highlights

**3-Axis Memory Model** (Type × Scope × Tier):
- **Type**: Episodic, Semantic, Procedural, Fact, Reflection (Tulving)
- **Scope**: Turn, Topic, Session, User (temporal reach)
- **Tier**: Buffer, Short, Long, Archive (Atkinson-Shiffrin + Baddeley)

```
Tier Promotion Pipeline:
┌─────────────────────────────────────────────────────────┐
│  Buffer (T0) - Sensory Store (Atkinson-Shiffrin)        │
│  TTL: 60s idle OR 500 tokens OR 3 turns                 │
├─────────────────────────────────────────────────────────┤
│  Short (T1) - Working Memory (Baddeley's 7±2)           │
│  Capacity: 9 items, auto-promote to Long when exceeded  │
├─────────────────────────────────────────────────────────┤
│  Long (T2) - Episodic Memory (Tulving)                  │
│  Session-level events and experiences                   │
├─────────────────────────────────────────────────────────┤
│  Archive (T3) - Semantic Memory (Tulving)               │
│  Promotion: Confidence ≥ 0.8 AND Confirms ≥ 3           │
└─────────────────────────────────────────────────────────┘
```

### Breaking Changes

None in this release.

### Dependencies

- .NET 10.0
- ModelContextProtocol 0.5.0-preview.1
- Microsoft.Extensions.VectorData.Abstractions 9.7.0
- LMSupply 0.8.10
- OpenAI 2.8.0
- Swashbuckle.AspNetCore 10.1.0

---

## [v0.3.0] - Previous Release

See git history for earlier changes.
