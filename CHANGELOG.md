# Changelog

All notable changes to Memory Indexer are documented here.

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

#### Technical Details
- All tools use existing SDK intelligence services (no new implementations)
- GraphTraversalTools uses `IMemoryGraphService`, `IImportancePropagator`, and `ICommunityDetector`
- ConflictResolutionTools uses `IContradictionDetector` and `IContradictionResolver`
- AdaptiveRetrievalTools uses `IQueryIntentClassifier` and `TieredMemoryRetriever`
- Tests: All 1029 tests passing (216 core + 813 SDK)

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
