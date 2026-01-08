# Changelog

All notable changes to Memory Indexer are documented here.

## [v0.4.0] - 2026-01-09

### Cognitive Architecture Completion

This release completes the 4-Tier Cognitive Memory Architecture with full tier promotion pipeline and cognitive compliance validation.

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

```
4-Tier Cognitive Memory Architecture:
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
- Microsoft.Extensions.VectorData.Abstractions 9.5.0
- LMSupply 0.8.5

---

## [v0.3.0] - Previous Release

See git history for earlier changes.
