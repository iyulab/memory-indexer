# Phase 22 Implementation Plan
**Date**: 2026-01-06
**Goal**: Control memory growth rate and optimize query-aware retrieval performance

## Executive Summary

Phase 22는 Twenty Questions Game 분석에서 발견된 3가지 critical issues를 해결합니다:

1. **Memory Growth Rate Control**: 6.8/round → 4.0/round (41% reduction)
2. **Recall Latency Optimization**: Avg 410ms → <200ms, P95 867ms → <500ms
3. **Query Intent-Aware Retrieval**: 특정 쿼리에 대한 관련 메모리 boosting 개선

## Project Philosophy Alignment

### Zero Context Engineering
- **적용**: 사용자가 growth policy나 latency budget을 설정하지 않음
- **구현**: 자동 adaptive growth control 및 latency budget management

### Intelligent Placement
- **적용**: Importance-based storage filtering으로 저품질 메모리 자동 필터링
- **구현**: MinImportanceForStorage threshold (default: 0.3)

### Hierarchical Memory
- **적용**: Tier-aware latency budgets (Working: 100ms, Session: 300ms, User: 500ms)
- **구현**: Progressive recall with early termination

### Proactive Consolidation
- **적용**: Dynamic promotion thresholds based on memory pressure
- **구현**: Adaptive threshold adjustment (압력 높을수록 promotion 기준 상향)

---

## Phase 22.1: Memory Growth Rate Control

### Current Issues (from Analysis)
- **Actual growth**: 6.8 memories/round (136 memories in 20 rounds)
- **Expected growth**: 4.0 memories/round (80 memories in 20 rounds)
- **Overgrowth**: +70% (56 excess memories)

### Root Causes
1. **No storage filtering**: Low-importance memories stored unconditionally
2. **No growth limits**: Unlimited memory creation per tier
3. **Fixed promotion thresholds**: Don't adapt to memory pressure

### Implementation Tasks

#### Task 1.1: Importance-Based Storage Filtering
**File**: `src/MemoryIndexer/Services/MemoryService.cs`

```csharp
public async Task<Guid> StoreAsync(MemoryUnit memory, ...)
{
    // NEW: Filter low-importance memories
    if (memory.ImportanceScore < _options.MemoryGrowth.MinImportanceForStorage)
    {
        _logger.LogDebug(
            "Skipping storage of low-importance memory (importance: {Importance}, threshold: {Threshold})",
            memory.ImportanceScore, _options.MemoryGrowth.MinImportanceForStorage);
        return Guid.Empty; // Signal: not stored
    }

    // Existing deduplication and storage logic...
}
```

**Expected Impact**: 20-30% reduction in storage calls

#### Task 1.2: Topic-Based Pre-Storage Deduplication
**File**: `src/MemoryIndexer.Sdk/Intelligence/Deduplication/DeduplicationService.cs`

```csharp
public async Task<DuplicateCheckResult> CheckTopicDuplicateAsync(
    MemoryUnit newMemory,
    IReadOnlyList<MemoryUnit> recentMemories,
    CancellationToken cancellationToken = default)
{
    // Extract topic from content (first 50 chars or metadata)
    var newTopic = ExtractTopic(newMemory.Content);

    // Find memories with same topic within lookback window
    var sameTopicMemories = recentMemories
        .Where(m => ExtractTopic(m.Content) == newTopic)
        .ToList();

    if (sameTopicMemories.Any())
    {
        // Topic already covered recently, skip storage
        return DuplicateCheckResult.SkipStorage("Topic recently covered");
    }

    return DuplicateCheckResult.Proceed();
}
```

**Expected Impact**: 15-20% reduction for repetitive topic discussions

#### Task 1.3: Dynamic Promotion Thresholds
**File**: `src/MemoryIndexer/Configuration/MemoryIndexerOptions.cs`

```csharp
public sealed class MemoryGrowthOptions
{
    public float MaxGrowthRatePerRound { get; set; } = 4.0f;
    public float MinImportanceForStorage { get; set; } = 0.3f;
    public bool TopicBasedDedup { get; set; } = true;
    public bool DynamicThresholds { get; set; } = true;

    // Adaptive threshold adjustment factors
    public float LowPressureThresholdMultiplier { get; set; } = 0.8f;  // Easier promotion
    public float HighPressureThresholdMultiplier { get; set; } = 1.5f; // Harder promotion
}
```

**File**: `src/MemoryIndexer.Sdk/Intelligence/Promotion/AdaptiveThresholdManager.cs` (NEW)

```csharp
public sealed class AdaptiveThresholdManager
{
    public float AdjustPromotionThreshold(
        float baseThreshold,
        MemoryPressure pressure)
    {
        return pressure switch
        {
            MemoryPressure.Low => baseThreshold * 0.8f,      // Easier (promote more)
            MemoryPressure.Medium => baseThreshold,          // Unchanged
            MemoryPressure.High => baseThreshold * 1.2f,     // Harder (promote less)
            MemoryPressure.Critical => baseThreshold * 1.5f, // Much harder
            _ => baseThreshold
        };
    }
}

public enum MemoryPressure
{
    Low,      // < 60% capacity
    Medium,   // 60-80% capacity
    High,     // 80-95% capacity
    Critical  // > 95% capacity
}
```

**Expected Impact**: Automatic growth control under memory pressure

#### Task 1.4: Growth Rate Monitoring
**File**: `src/MemoryIndexer.Sdk/Observability/MemoryGrowthMonitor.cs` (NEW)

```csharp
public sealed class MemoryGrowthMonitor
{
    private readonly Dictionary<string, List<DateTime>> _storageTimestamps = new();

    public float CalculateGrowthRate(string userId, TimeSpan window)
    {
        if (!_storageTimestamps.TryGetValue(userId, out var timestamps))
            return 0f;

        var recentStores = timestamps
            .Where(t => DateTime.UtcNow - t <= window)
            .Count();

        return recentStores / (float)window.TotalMinutes;
    }

    public bool IsGrowthRateExceeded(string userId, float maxRate)
    {
        var currentRate = CalculateGrowthRate(userId, TimeSpan.FromMinutes(10));
        return currentRate > maxRate;
    }
}
```

### Test Coverage
- `ImportanceBasedFilteringTests.cs`: MinImportanceForStorage 경계 테스트
- `TopicDeduplicationTests.cs`: Topic extraction 및 dedup 테스트
- `AdaptiveThresholdTests.cs`: Pressure-based threshold adjustment
- `GrowthRateMonitorTests.cs`: Growth rate calculation 테스트

---

## Phase 22.2: Recall Latency Optimization

### Current Issues (from Analysis)
- **Latency variance**: 10x (77ms to 867ms)
- **Average latency**: 410ms
- **StdDev**: ~200ms (high unpredictability)

### Performance Breakdown (from logs)
```
[RECALL] Embedding: 45ms
[RECALL] Search: 150ms
[RECALL] Scoring: 69ms
Total: 264ms
```

### Implementation Tasks

#### Task 2.1: Embedding Cache with LRU Eviction
**File**: `src/MemoryIndexer.Sdk/Embedding/Providers/CachedEmbeddingService.cs` (NEW)

```csharp
public sealed class CachedEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _innerService;
    private readonly LruCache<string, ReadOnlyMemory<float>> _cache;

    public CachedEmbeddingService(
        IEmbeddingService innerService,
        IOptions<EmbeddingCacheOptions> options)
    {
        _innerService = innerService;
        _cache = new LruCache<string, ReadOnlyMemory<float>>(
            capacity: options.Value.CacheSize);
    }

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = HashText(text);

        if (_cache.TryGet(cacheKey, out var cached))
        {
            _metrics.RecordCacheHit();
            return cached;
        }

        var embedding = await _innerService.GenerateEmbeddingAsync(text, cancellationToken);
        _cache.Add(cacheKey, embedding);
        _metrics.RecordCacheMiss();

        return embedding;
    }
}
```

**Expected Impact**: Embedding time 45ms → 1-5ms (90% reduction on cache hit)

#### Task 2.2: Batch Query Processing
**File**: `src/MemoryIndexer.Sdk/Intelligence/Search/BatchQueryProcessor.cs` (NEW)

```csharp
public sealed class BatchQueryProcessor
{
    public async Task<Dictionary<string, List<MemoryUnit>>> ProcessBatchAsync(
        IReadOnlyList<(string Query, int Limit)> queries,
        CancellationToken cancellationToken = default)
    {
        // Generate embeddings in parallel
        var embeddingTasks = queries.Select(q =>
            _embeddingService.GenerateEmbeddingAsync(q.Query, cancellationToken));
        var embeddings = await Task.WhenAll(embeddingTasks);

        // Perform batch vector search
        var results = await _memoryStore.SearchBatchAsync(embeddings, queries.Select(q => q.Limit));

        return results;
    }
}
```

**Expected Impact**: Multiple queries in single round → parallel processing

#### Task 2.3: Early Termination for Sufficient Results
**File**: `src/MemoryIndexer.Sdk/Intelligence/Search/HybridSearchService.cs`

```csharp
public async Task<List<MemoryUnit>> SearchWithEarlyTerminationAsync(
    string query,
    int limit,
    float sufficientScoreThreshold = 0.9f,
    CancellationToken cancellationToken = default)
{
    var results = new List<MemoryUnit>();

    // Stage 1: Exact keyword match (fast path)
    var exactMatches = await GetExactKeywordMatchesAsync(query, limit);
    if (exactMatches.Any(m => m.Score >= sufficientScoreThreshold))
    {
        // Found high-confidence results, skip expensive semantic search
        return exactMatches.Take(limit).ToList();
    }

    // Stage 2: Semantic search (only if needed)
    var semanticResults = await GetSemanticMatchesAsync(query, limit);
    results.AddRange(exactMatches);
    results.AddRange(semanticResults);

    return results.OrderByDescending(m => m.Score).Take(limit).ToList();
}
```

**Expected Impact**: High-confidence queries skip semantic search (50% latency reduction)

#### Task 2.4: Tier-Specific Latency Budgets
**File**: `src/MemoryIndexer/Configuration/VCMOptions.cs`

```csharp
public sealed class LatencyBudgetOptions
{
    public int WorkingMemoryMaxMs { get; set; } = 100;   // Hot path
    public int SessionMemoryMaxMs { get; set; } = 300;   // Warm path
    public int UserProfileMaxMs { get; set; } = 500;     // Cold path
    public bool EnableEarlyTermination { get; set; } = true;
}
```

**File**: `src/MemoryIndexer/Services/VirtualContextManager.cs`

```csharp
public async Task<List<MemoryUnit>> RecallWithBudgetAsync(
    string query,
    int limit,
    CancellationToken cancellationToken = default)
{
    var sw = Stopwatch.StartNew();
    var results = new List<MemoryUnit>();

    // Tier 1: Working Memory (budget: 100ms)
    var working = await _workingMemory.RecallAsync(query, limit, cancellationToken);
    results.AddRange(working);
    if (sw.ElapsedMilliseconds > _budgetOptions.WorkingMemoryMaxMs)
        return results.Take(limit).ToList();

    // Tier 2: Session Memory (budget: 300ms total)
    var session = await _sessionStore.SearchAsync(query, limit - results.Count, cancellationToken);
    results.AddRange(session);
    if (sw.ElapsedMilliseconds > _budgetOptions.SessionMemoryMaxMs)
        return results.Take(limit).ToList();

    // Tier 3: User Profile (budget: 500ms total)
    var profile = await _userProfile.RecallAsync(query, limit - results.Count, cancellationToken);
    results.AddRange(profile);

    return results.Take(limit).ToList();
}
```

**Expected Impact**: Guaranteed max latency per tier, better SLA predictability

### Test Coverage
- `EmbeddingCacheTests.cs`: LRU cache behavior, hit/miss metrics
- `BatchQueryProcessorTests.cs`: Parallel processing correctness
- `EarlyTerminationTests.cs`: Score threshold behavior
- `LatencyBudgetTests.cs`: Tier budget enforcement

---

## Phase 22.3: Query Intent-Aware Retrieval Enhancement

### Current Issues (from Analysis)
- Generic queries work (procedural memories ranked high)
- Specific queries don't boost relevant memories effectively
- No query type classification

### Implementation Tasks

#### Task 3.1: Enhanced Intent Classification
**File**: `src/MemoryIndexer.Sdk/Intelligence/Classification/LocalQueryIntentClassifier.cs`

```csharp
public sealed class LocalQueryIntentClassifier
{
    public QueryIntent ClassifyIntent(string query)
    {
        var intent = new QueryIntent();

        // Factual intent (who, what, when, where)
        if (Regex.IsMatch(query, @"\b(who|what|when|where|which)\b", RegexOptions.IgnoreCase))
        {
            intent.PrimaryIntent = IntentType.Factual;
            intent.Confidence = 0.9f;
        }
        // Procedural intent (how, steps, process)
        else if (Regex.IsMatch(query, @"\b(how|steps|process|procedure|instructions)\b", RegexOptions.IgnoreCase))
        {
            intent.PrimaryIntent = IntentType.Procedural;
            intent.Confidence = 0.85f;
        }
        // Contextual intent (recent, current, now)
        else if (Regex.IsMatch(query, @"\b(recent|current|now|latest|today)\b", RegexOptions.IgnoreCase))
        {
            intent.PrimaryIntent = IntentType.Contextual;
            intent.Confidence = 0.8f;
        }
        // Temporal intent (before, after, during)
        else if (Regex.IsMatch(query, @"\b(before|after|during|since|until)\b", RegexOptions.IgnoreCase))
        {
            intent.PrimaryIntent = IntentType.Temporal;
            intent.Confidence = 0.75f;
        }
        else
        {
            intent.PrimaryIntent = IntentType.General;
            intent.Confidence = 0.5f;
        }

        // Detect secondary intent
        DetectSecondaryIntent(query, intent);

        return intent;
    }
}

public sealed class QueryIntent
{
    public IntentType PrimaryIntent { get; set; }
    public IntentType? SecondaryIntent { get; set; }
    public float Confidence { get; set; }
}

public enum IntentType
{
    General,
    Factual,      // What/Who questions
    Procedural,   // How-to queries
    Contextual,   // Recent events
    Temporal      // Time-based queries
}
```

#### Task 3.2: Intent-Specific K Values
**File**: `src/MemoryIndexer/Configuration/SearchOptions.cs`

```csharp
public sealed class IntentBasedRetrievalOptions
{
    public int FactualK { get; set; } = 5;        // Precise, fewer results
    public int ProceduralK { get; set; } = 3;     // Specific procedures
    public int ContextualK { get; set; } = 10;    // Recent events, more context
    public int TemporalK { get; set; } = 15;      // Timeline, many events
    public int GeneralK { get; set; } = 10;       // Default
}
```

**File**: `src/MemoryIndexer.Sdk/Intelligence/Search/HybridSearchService.cs`

```csharp
public async Task<List<MemoryUnit>> SearchByIntentAsync(
    string query,
    int requestedLimit,
    CancellationToken cancellationToken = default)
{
    // Classify query intent
    var intent = _intentClassifier.ClassifyIntent(query);

    // Adjust K based on intent
    var adjustedLimit = intent.PrimaryIntent switch
    {
        IntentType.Factual => Math.Min(requestedLimit, _intentOptions.FactualK),
        IntentType.Procedural => Math.Min(requestedLimit, _intentOptions.ProceduralK),
        IntentType.Contextual => Math.Min(requestedLimit, _intentOptions.ContextualK),
        IntentType.Temporal => Math.Min(requestedLimit, _intentOptions.TemporalK),
        _ => requestedLimit
    };

    // Perform search with adjusted limit
    var results = await SearchAsync(query, adjustedLimit, cancellationToken);

    // Apply intent-specific boosting
    ApplyIntentBoosting(results, intent);

    return results.OrderByDescending(m => m.Score).Take(requestedLimit).ToList();
}
```

#### Task 3.3: Entity-Based Boosting
**File**: `src/MemoryIndexer.Sdk/Intelligence/Search/EntityBooster.cs` (NEW)

```csharp
public sealed class EntityBooster
{
    public void ApplyEntityBoosting(
        List<MemoryUnit> memories,
        string query,
        float boostFactor = 0.3f)
    {
        // Extract entities from query (simple NER: capitalized words)
        var queryEntities = ExtractEntities(query);

        foreach (var memory in memories)
        {
            // Extract entities from memory content
            var memoryEntities = ExtractEntities(memory.Content);

            // Calculate entity overlap
            var overlap = queryEntities.Intersect(memoryEntities).Count();
            var entityBoost = overlap * boostFactor;

            // Apply boost (stored in metadata for transparency)
            if (memory.Metadata == null)
                memory.Metadata = new Dictionary<string, object>();

            memory.Metadata["EntityBoost"] = entityBoost;
            memory.ImportanceScore += entityBoost;
        }
    }

    private HashSet<string> ExtractEntities(string text)
    {
        // Simple entity extraction: capitalized words
        return new HashSet<string>(
            Regex.Matches(text, @"\b[A-Z][a-z]+\b")
                .Select(m => m.Value),
            StringComparer.OrdinalIgnoreCase);
    }
}
```

### Test Coverage
- `QueryIntentClassifierTests.cs`: Intent detection accuracy
- `IntentBasedRetrievalTests.cs`: K value adjustment per intent
- `EntityBoosterTests.cs`: Entity extraction and boosting

---

## Implementation Sequence

### Week 1: Phase 22.1 (Memory Growth Control)
1. Day 1-2: Importance filtering + tests
2. Day 3-4: Topic-based deduplication + tests
3. Day 5: Dynamic thresholds + growth monitoring + tests

### Week 2: Phase 22.2 (Latency Optimization)
1. Day 1-2: Embedding cache + tests
2. Day 3-4: Batch processing + early termination + tests
3. Day 5: Latency budgets + integration + tests

### Week 3: Phase 22.3 (Query Intent)
1. Day 1-2: Enhanced intent classifier + tests
2. Day 3-4: Intent-based K values + entity boosting + tests
3. Day 5: Integration testing + documentation

---

## Success Metrics

### Memory Growth (Phase 22.1)
- ✅ Growth rate ≤ 4.5 memories/round (target: 4.0)
- ✅ Storage filter effectiveness > 20%
- ✅ Dynamic threshold adaptation verified

### Latency (Phase 22.2)
- ✅ Average recall latency < 200ms (current: 410ms)
- ✅ P95 recall latency < 500ms (current: 867ms)
- ✅ Embedding cache hit rate > 60%
- ✅ Early termination effectiveness > 30%

### Query Intent (Phase 22.3)
- ✅ Intent classification accuracy > 85%
- ✅ Query-specific boosting measurable improvement
- ✅ Entity-based boosting effectiveness > 15%

---

## Risk Assessment

### High Risk
- **Embedding cache invalidation**: Stale embeddings if content changes
  - Mitigation: Cache TTL (1 hour default), version-based invalidation

### Medium Risk
- **Early termination false positives**: Skip semantic search prematurely
  - Mitigation: Configurable threshold, A/B testing, metrics monitoring

### Low Risk
- **Dynamic thresholds over-adjustment**: Too aggressive under pressure
  - Mitigation: Gradual adjustment, min/max bounds, manual override

---

## Documentation Updates

### ROADMAP.md
- Update Phase 22 status: Planned → In Progress → Completed
- Add implementation details for each sub-phase
- Update test coverage section

### ARCHITECTURE.md
- Add Memory Growth section (filtering, thresholds)
- Add Latency Optimization section (cache, budgets)
- Add Query Intent section (classification, boosting)

### API Documentation
- Document new configuration options
- Add examples for memory growth policies
- Add latency budget usage examples

---

## Testing Strategy

### Unit Tests (Target: +40 tests)
- Phase 22.1: 15 tests
- Phase 22.2: 15 tests
- Phase 22.3: 10 tests

### Integration Tests
- End-to-end growth control validation
- Latency budget enforcement tests
- Query intent flow tests

### Performance Tests
- Growth rate benchmarks (4-round simulation)
- Latency benchmarks (1000 query test)
- Cache effectiveness measurement

### Validation
- Twenty Questions Game re-run
- Before/After comparison
- Success criteria validation
