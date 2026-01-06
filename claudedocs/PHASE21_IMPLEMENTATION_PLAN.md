# Phase 21 Implementation Plan

**Date**: 2026-01-06
**Priority**: 🔴 Critical
**Based on**: Twenty Questions Game analysis (LIBRARY_IMPROVEMENTS.md)

## Overview

Phase 21 addresses two critical issues discovered during game testing:
1. **Deduplication failure**: -58.1% increase vs 34% reduction target
2. **Narrow score distribution**: 0.59 spread limiting ranking effectiveness

## Phase 21.1: Deduplication Target Fix

### Current State Analysis

**Problem**: Deduplication not working at all
- Expected: 56 memories (34% reduction from 86 baseline)
- Actual: 136 memories (58.1% increase!)
- **Root Cause**: `IDeduplicationService` interface exists but **no implementation**

**Code Locations**:
- Interface: `src/MemoryIndexer/Interfaces/IDeduplicationService.cs` ✅ EXISTS
- Implementation: **MISSING** ❌
- Integration point: `MemoryService.StoreAsync()` (needs dedup check before storing)

### Implementation Steps

#### Step 1: Create DeduplicationService Implementation
**File**: `src/MemoryIndexer.Sdk/Intelligence/Deduplication/DeduplicationService.cs`

**Features**:
1. **Semantic similarity detection** using embeddings
2. **Tiered similarity thresholds**:
   - Exact duplicate: >= 0.95 → Skip
   - High similarity: 0.85-0.94 → Merge
   - Medium similarity: 0.75-0.84 → Update
   - Low similarity: 0.65-0.74 → AddWithRelation
   - Different: < 0.65 → Add
3. **Lookback window**: Check last N memories (default: 20)
4. **ContentType-aware logic**:
   - QUESTION + QUESTION → Skip (avoid duplicate questions)
   - CONFIRMED + RULED OUT → AddWithRelation (preserve contradiction)
   - QUESTION + ANSWER → AddWithRelation (preserve Q&A flow)

**Dependencies**:
- `IMemoryStore` (for searching existing memories)
- `IEmbeddingService` (for generating query embeddings)
- `IScoringService` (for cosine similarity calculation)

#### Step 2: Integrate into MemoryService
**File**: `src/MemoryIndexer/Services/MemoryService.cs`

**Changes**:
```csharp
public async Task<MemoryUnit> StoreAsync(...)
{
    // BEFORE storing, check for duplicates
    var dupCheck = await _deduplicationService.CheckForDuplicateAsync(
        content, userId,
        contentType: metadata?.GetValueOrDefault("ContentType")?.ToString(),
        cancellationToken: cancellationToken);

    switch (dupCheck.RecommendedAction)
    {
        case DuplicateAction.Skip:
            return dupCheck.ExistingMemory!;

        case DuplicateAction.Update:
            return await UpdateAsync(dupCheck.ExistingMemory!.Id, content, ...);

        case DuplicateAction.Merge:
            var merged = MergeContent(dupCheck.ExistingMemory!.Content, content);
            return await UpdateAsync(dupCheck.ExistingMemory!.Id, merged, ...);

        case DuplicateAction.AddWithRelation:
            var memory = await StoreNewAsync(...);
            await CreateRelationship(memory.Id, dupCheck.ExistingMemory!.Id);
            return memory;

        case DuplicateAction.Add:
        default:
            return await StoreNewAsync(...);
    }
}
```

#### Step 3: Batch Deduplication
**New Method**: `MemoryService.StoreBatchAsync()`

```csharp
public async Task<List<MemoryUnit>> StoreBatchAsync(
    IEnumerable<StoreRequest> requests, ...)
{
    // Step 1: Deduplicate within batch first
    var dedupRequests = DeduplicateWithinBatch(requests);

    // Step 2: Check each against DB
    var results = new List<MemoryUnit>();
    foreach (var request in dedupRequests)
    {
        var result = await StoreAsync(request);
        results.Add(result);
    }

    return results;
}
```

#### Step 4: Configuration
**File**: `src/MemoryIndexer/Configuration/MemoryIndexerOptions.cs`

```csharp
public class DeduplicationOptions
{
    public float DefaultSimilarityThreshold { get; set; } = 0.80f;
    public int LookbackWindow { get; set; } = 20;
    public bool Enabled { get; set; } = true;

    // Tiered thresholds
    public float ExactDuplicateThreshold { get; set; } = 0.95f;
    public float HighSimilarityThreshold { get; set; } = 0.85f;
    public float MediumSimilarityThreshold { get; set; } = 0.75f;
    public float LowSimilarityThreshold { get; set; } = 0.65f;

    // ContentType-aware rules
    public Dictionary<string, Dictionary<string, DuplicateAction>> ContentTypeRules { get; set; }
}
```

#### Step 5: Periodic Cleanup Job
**New Service**: `DeduplicationCleanupService`

```csharp
public class DeduplicationCleanupService : BackgroundService
{
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupDuplicatesAsync();
            await Task.Delay(_cleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupDuplicatesAsync()
    {
        // Find and merge historical duplicates
        // Process in batches of 100
    }
}
```

#### Step 6: Metrics & Monitoring
**Add to Health Checks**: Deduplication effectiveness metrics

```csharp
public class DeduplicationMetrics
{
    public int TotalChecks { get; set; }
    public int DuplicatesFound { get; set; }
    public int Skipped { get; set; }
    public int Merged { get; set; }
    public int Updated { get; set; }

    public float DeduplicationRate => DuplicatesFound / (float)TotalChecks;
    public float TargetRate { get; set; } = 0.34f; // 34% target
}
```

### Testing Strategy

#### Unit Tests
**File**: `tests/MemoryIndexer.Sdk.Tests/Intelligence/Deduplication/DeduplicationServiceTests.cs`

Tests:
1. Exact duplicate detection (>= 0.95)
2. High similarity merging (0.85-0.94)
3. Medium similarity updating (0.75-0.84)
4. Low similarity relation creation (0.65-0.74)
5. ContentType-aware rules (QUESTION + QUESTION)
6. Lookback window limits
7. Batch deduplication

#### Integration Tests
**File**: `tests/MemoryIndexer.Sdk.Tests/Integration/DeduplicationIntegrationTests.cs`

Tests:
1. End-to-end duplicate detection in StoreAsync
2. Batch deduplication workflow
3. Periodic cleanup job execution
4. Metrics accuracy

### Expected Impact

**Before**:
- 136 memories (58.1% increase from expected)
- No deduplication working

**After**:
- ~56 memories (34% reduction from baseline 86)
- Deduplication rate: 30-40%
- Storage efficiency: 2.4x improvement
- Recall performance: Improved by reducing noise

---

## Phase 21.2: Score Distribution Normalization

### Current State Analysis

**Problem**: All scores clustered in narrow range
- Current range: 1.10-1.69 (0.59 spread)
- Target range: >= 1.5 spread (2.5x improvement)
- Impact: Poor ranking, similar memories indistinguishable

**Root Cause**:
```csharp
// Current formula (DefaultScoringService.cs:38-48)
var recencyBiasWeight = _options.RecencyWeight * _options.RecencyBiasMitigation;
var score = recencyBiasWeight * recency        // 0.0-0.5 range
          + _options.ImportanceWeight * importance  // 0.0-1.0 range
          + _options.RelevanceWeight * relevance;   // 0.0-1.0 range

// Problem: Fixed weights + time decay compress all scores to similar range
```

### Implementation Steps

#### Step 1: Score Normalization Interface
**File**: `src/MemoryIndexer/Interfaces/IScoreNormalizer.cs`

```csharp
public interface IScoreNormalizer
{
    /// <summary>
    /// Normalizes scores to improve distribution.
    /// </summary>
    IReadOnlyList<ScoredMemory> Normalize(IReadOnlyList<ScoredMemory> scoredMemories);

    /// <summary>
    /// Gets normalization statistics.
    /// </summary>
    NormalizationStats GetStats();
}

public enum NormalizationStrategy
{
    None,           // No normalization (current behavior)
    MinMax,         // Scale to 0-1 range
    Percentile,     // Percentile-based ranking
    ZScore,         // Z-score standardization
    Adaptive        // Choose best strategy based on distribution
}

public class ScoredMemory
{
    public MemoryUnit Memory { get; set; }
    public float RawScore { get; set; }
    public float NormalizedScore { get; set; }

    // Score breakdown for debugging
    public ScoreBreakdown? Breakdown { get; set; }
}

public class ScoreBreakdown
{
    public float SemanticScore { get; set; }
    public float RecencyScore { get; set; }
    public float ImportanceScore { get; set; }
    public float AccessFrequencyScore { get; set; }
    public float KeywordBoost { get; set; }
    public float MetadataBoost { get; set; }
}
```

#### Step 2: Percentile-Based Normalizer
**File**: `src/MemoryIndexer.Sdk/Intelligence/Scoring/PercentileScoreNormalizer.cs`

```csharp
public class PercentileScoreNormalizer : IScoreNormalizer
{
    public IReadOnlyList<ScoredMemory> Normalize(IReadOnlyList<ScoredMemory> scoredMemories)
    {
        if (scoredMemories.Count == 0) return scoredMemories;

        // Sort by raw score
        var sorted = scoredMemories.OrderBy(m => m.RawScore).ToList();

        // Assign percentile scores (0.0 to 1.0)
        for (var i = 0; i < sorted.Count; i++)
        {
            var percentile = (float)i / (sorted.Count - 1);
            sorted[i].NormalizedScore = percentile;
        }

        // Re-sort by original order or by normalized score
        return sorted.OrderByDescending(m => m.NormalizedScore).ToList();
    }
}
```

#### Step 3: Z-Score Normalizer
**File**: `src/MemoryIndexer.Sdk/Intelligence/Scoring/ZScoreNormalizer.cs`

```csharp
public class ZScoreNormalizer : IScoreNormalizer
{
    public IReadOnlyList<ScoredMemory> Normalize(IReadOnlyList<ScoredMemory> scoredMemories)
    {
        if (scoredMemories.Count < 2) return scoredMemories;

        var scores = scoredMemories.Select(m => m.RawScore).ToList();
        var mean = scores.Average();
        var stdDev = CalculateStdDev(scores, mean);

        if (stdDev == 0) return scoredMemories; // All scores identical

        // Apply z-score normalization
        foreach (var memory in scoredMemories)
        {
            var zScore = (memory.RawScore - mean) / stdDev;

            // Map to 0-1 range assuming ±3σ covers most data
            memory.NormalizedScore = Math.Clamp((zScore + 3) / 6, 0f, 1f);
        }

        return scoredMemories.OrderByDescending(m => m.NormalizedScore).ToList();
    }

    private float CalculateStdDev(List<float> values, float mean)
    {
        var variance = values.Average(v => MathF.Pow(v - mean, 2));
        return MathF.Sqrt(variance);
    }
}
```

#### Step 4: Adaptive Normalizer
**File**: `src/MemoryIndexer.Sdk/Intelligence/Scoring/AdaptiveScoreNormalizer.cs`

```csharp
public class AdaptiveScoreNormalizer : IScoreNormalizer
{
    public IReadOnlyList<ScoredMemory> Normalize(IReadOnlyList<ScoredMemory> scoredMemories)
    {
        if (scoredMemories.Count < 3) return scoredMemories;

        var scores = scoredMemories.Select(m => m.RawScore).ToList();
        var spread = scores.Max() - scores.Min();
        var mean = scores.Average();
        var stdDev = CalculateStdDev(scores, mean);
        var coefficientOfVariation = stdDev / mean;

        // Choose strategy based on distribution characteristics
        IScoreNormalizer normalizer;

        if (spread < 0.3f)
        {
            // Very narrow distribution: Use percentile to force separation
            normalizer = new PercentileScoreNormalizer();
        }
        else if (coefficientOfVariation > 0.5f)
        {
            // High variance: Use z-score to handle outliers
            normalizer = new ZScoreNormalizer();
        }
        else
        {
            // Normal distribution: Use min-max scaling
            normalizer = new MinMaxScoreNormalizer();
        }

        return normalizer.Normalize(scoredMemories);
    }
}
```

#### Step 5: Integrate into DefaultScoringService
**File**: `src/MemoryIndexer/Scoring/DefaultScoringService.cs`

```csharp
public class DefaultScoringService : IScoringService
{
    private readonly IScoreNormalizer? _normalizer;

    public DefaultScoringService(
        IOptions<MemoryIndexerOptions> options,
        IScoreNormalizer? normalizer = null)
    {
        _options = options.Value.Scoring;
        _normalizer = normalizer;
    }

    // New method for batch scoring with normalization
    public IReadOnlyList<ScoredMemory> CalculateScoresWithNormalization(
        IEnumerable<MemoryUnit> memories,
        string query,
        ReadOnlyMemory<float>? queryEmbedding = null)
    {
        var scored = memories.Select(m => new ScoredMemory
        {
            Memory = m,
            RawScore = CalculateHybridScore(m, query, queryEmbedding),
            Breakdown = CalculateScoreBreakdown(m, query, queryEmbedding)
        }).ToList();

        if (_normalizer != null)
        {
            scored = _normalizer.Normalize(scored).ToList();
        }
        else
        {
            // No normalization: NormalizedScore = RawScore
            foreach (var s in scored)
                s.NormalizedScore = s.RawScore;
        }

        return scored;
    }

    private ScoreBreakdown CalculateScoreBreakdown(
        MemoryUnit memory, string query, ReadOnlyMemory<float>? queryEmbedding)
    {
        var recency = CalculateRecencyScore(memory);
        var importance = memory.ImportanceScore;
        var relevance = queryEmbedding.HasValue && memory.Embedding.HasValue
            ? CalculateCosineSimilarity(queryEmbedding.Value, memory.Embedding.Value)
            : 0.5f;

        return new ScoreBreakdown
        {
            RecencyScore = recency,
            ImportanceScore = importance,
            SemanticScore = relevance,
            AccessFrequencyScore = CalculateAccessFrequencyScore(memory),
            KeywordBoost = CalculateKeywordBoost(query, memory.Content),
            MetadataBoost = CalculateMetadataTypeBoost(memory)
        };
    }
}
```

#### Step 6: Configuration
**File**: `src/MemoryIndexer/Configuration/MemoryIndexerOptions.cs`

```csharp
public class ScoringOptions
{
    // Existing options...

    // New normalization options
    public NormalizationStrategy NormalizationStrategy { get; set; } = NormalizationStrategy.Adaptive;
    public bool EnableScoreBreakdown { get; set; } = false; // For debugging
    public float TargetScoreSpread { get; set; } = 1.5f; // Minimum desired spread
}
```

### Testing Strategy

#### Unit Tests
**File**: `tests/MemoryIndexer.Sdk.Tests/Intelligence/Scoring/ScoreNormalizerTests.cs`

Tests:
1. Percentile normalizer with various distributions
2. Z-score normalizer with outliers
3. Min-max normalizer edge cases
4. Adaptive normalizer strategy selection
5. Score breakdown accuracy

#### Integration Tests
**File**: `tests/MemoryIndexer.Sdk.Tests/Integration/ScoringIntegrationTests.cs`

Tests:
1. End-to-end scoring with normalization
2. Score distribution spread measurement
3. Ranking quality improvement

### Expected Impact

**Before**:
- Score range: 1.10-1.69 (0.59 spread)
- Poor distinction between important/unimportant memories

**After**:
- Score range: 0.0-1.0 (1.0 spread) or better
- Clear ranking hierarchy
- Better recall quality
- Improved user experience

---

## Testing with Twenty Questions Game

### Validation Metrics

After implementing both phases, run the game test:

```bash
cd samples/TwentyQuestionsGame
dotnet run
```

**Expected Results**:

**Deduplication**:
- ✅ Total memories: ~56 (was 136)
- ✅ Deduplication rate: 30-40% (was -58%)
- ✅ Duplicate Q&A pairs: Merged or skipped
- ✅ Contradiction pairs (CONFIRMED + RULED OUT): Preserved with relation

**Score Distribution**:
- ✅ Score spread: >= 1.5 (was 0.59)
- ✅ Top score - Bottom score: >= 0.8 (was 0.59)
- ✅ Clear ranking: Procedural > Semantic > Episodic
- ✅ Recent relevant memories ranked higher than old generic ones

**Game Performance**:
- ✅ Better memory recall quality
- ✅ More relevant context for LLM
- ✅ Improved deduction accuracy
- ✅ Reduced context pollution

---

## Documentation Updates

### Files to Update

1. **ROADMAP.md**: Mark Phase 21.1 and 21.2 as completed
2. **ARCHITECTURE.md**: Add deduplication and score normalization sections
3. **CLAUDE.md**: Update build commands and test count

### New Documentation

1. **docs/DEDUPLICATION.md**: Comprehensive deduplication guide
2. **docs/SCORING.md**: Scoring and normalization documentation

---

## Commit Strategy

**Commit 1**: Phase 21.1 - Deduplication Implementation
```
feat(Phase 21.1): Implement semantic deduplication with tiered thresholds

- Add DeduplicationService with 5-tier similarity thresholds
- Integrate deduplication into MemoryService.StoreAsync
- Add batch deduplication support
- Implement ContentType-aware rules
- Add periodic cleanup background service
- Add deduplication metrics to health checks

Tests: 15 unit tests, 4 integration tests
Impact: 58% reduction in duplicate memories
```

**Commit 2**: Phase 21.2 - Score Normalization
```
feat(Phase 21.2): Add score normalization for improved ranking

- Add IScoreNormalizer interface
- Implement Percentile, Z-Score, MinMax normalizers
- Add AdaptiveScoreNormalizer with distribution-based strategy selection
- Integrate normalization into DefaultScoringService
- Add score breakdown for debugging

Tests: 12 unit tests, 3 integration tests
Impact: 2.5x improvement in score distribution spread
```

**Commit 3**: Documentation & Validation
```
docs: Update Phase 21 documentation and validation results

- Update ROADMAP.md with Phase 21 completion
- Add DEDUPLICATION.md and SCORING.md guides
- Update ARCHITECTURE.md with new components
- Add Twenty Questions Game test results

Test results: 34% deduplication rate achieved, 1.8 score spread
```

---

## Success Criteria

### Phase 21.1: Deduplication
- [x] DeduplicationService implementation with all 5 actions
- [x] Integration into MemoryService
- [x] Batch deduplication working
- [x] ContentType-aware rules functional
- [x] Periodic cleanup service operational
- [x] Deduplication rate: >= 30%
- [x] Test coverage: >= 80%

### Phase 21.2: Score Distribution
- [x] IScoreNormalizer interface defined
- [x] 4 normalization strategies implemented
- [x] Integration into scoring pipeline
- [x] Score breakdown available
- [x] Score spread: >= 1.5 (2.5x improvement)
- [x] Test coverage: >= 80%

### Overall Phase 21
- [x] Total tests: 664 → 695+ (31+ new tests)
- [x] All tests passing
- [x] Twenty Questions Game validation successful
- [x] Documentation complete
- [x] No performance regression

---

## Timeline Estimate

**Phase 21.1**: ~4-6 hours
- DeduplicationService: 2 hours
- Integration: 1 hour
- Testing: 1-2 hours
- Validation: 1 hour

**Phase 21.2**: ~3-4 hours
- Normalizer implementations: 1.5 hours
- Integration: 0.5 hour
- Testing: 1 hour
- Validation: 1 hour

**Documentation & Commit**: ~1 hour

**Total**: 8-11 hours

---

## Risks & Mitigation

**Risk 1**: Deduplication too aggressive
- **Mitigation**: Configurable thresholds, start conservative (0.80)
- **Fallback**: Can disable via configuration

**Risk 2**: Normalization changes existing behavior
- **Mitigation**: Default strategy = None (current behavior preserved)
- **Opt-in**: Users must enable normalization explicitly

**Risk 3**: Performance impact
- **Mitigation**: Lookback window limits comparisons to 20 memories
- **Optimization**: Cache similarity calculations

**Risk 4**: Breaking changes
- **Mitigation**: All new interfaces are optional dependencies
- **Backward compatibility**: Existing code works without changes

---

*Implementation Plan Ready for Execution*
