# Memory-Indexer Library Improvements
**Based on**: 20-round game log analysis (gpt-5.2 test)
**Focus**: General library improvements, not application-specific

## 1. Memory Scoring & Ranking Issues

### Issue 1.1: Narrow Score Distribution
**Observation from logs**:
```
Round 3 (13 memories):
[1.69] GAME_RULES (Procedural)
[1.47] STRATEGY_PHASE1 (Procedural)
[1.43] DEDUCTION_TEMPLATE (Procedural)
[1.36] STRATEGY_PHASE2 (Procedural)
[1.35] DEDUCTION_R2 (Semantic, CONFIRMED)
[1.31] DEDUCTION_R1 (Semantic, RULED OUT)
[1.27] QA_R2 (Episodic)
[1.21] ROUND 3 (Episodic)
[1.19] ROUND 1 (Episodic)
[1.19] ROUND 2 (Episodic)
[1.17] QA_R1 (Episodic)
[1.10] MY_QUESTION_R2 (Episodic)
[1.10] MY_QUESTION_R1 (Episodic)

Score Range: 1.10 - 1.69 (0.59 spread)
```

**Problem**:
- 모든 메모리 점수가 1.10-1.69 범위에 몰려있음
- 최고/최저 점수 차이가 0.59밖에 안됨 (35% 차이)
- Ranking이 명확하지 않음 → 검색 품질 저하 가능

**Root Cause Analysis**:
```
HybridScore = α·Semantic + β·Recency + γ·Importance + δ·AccessFrequency
            + KeywordBoost(0.5) + MetadataBoost(-0.3~+0.5)

Recency bias mitigation: 50% reduction
→ Recent memories도 점수가 크게 오르지 않음
→ Old memories도 점수가 크게 떨어지지 않음
```

**Impact**:
- 중요한 메모리와 덜 중요한 메모리 구분 어려움
- 15개 limit에서 잘못된 메모리가 포함될 위험
- Query relevance가 충분히 반영되지 않음

**Proposed Solutions**:

**A) Dynamic Score Normalization**
```csharp
// After calculating all scores, normalize to use full 0-1 range
var minScore = scores.Min();
var maxScore = scores.Max();
var normalized = scores.Select(s => (s - minScore) / (maxScore - minScore));
```

**B) Tiered Scoring System**
```csharp
// Different score ranges for different memory types
Procedural: 0.8-1.0 (always important)
Semantic: 0.4-0.9 (based on relevance)
Episodic: 0.1-0.6 (decay with time)
```

**C) Adaptive Recency Bias**
```csharp
// Adjust recency bias based on query type
Factual query: Low recency bias (0.7 reduction)
Contextual query: Medium recency bias (0.5 reduction)
Temporal query: High recency bias (0.2 reduction)
```

---

### Issue 1.2: Memory Type Score Clustering
**Observation**:
```
Procedural memories: 1.36-1.69 (always top)
Semantic memories: 1.31-1.35 (middle)
Episodic memories: 1.10-1.27 (bottom)
```

**Problem**:
- Memory type이 점수를 지배함
- Procedural이 항상 상위권 → 다양성 부족
- Recent episodic memories가 낮은 점수

**Impact**:
- 최근 중요한 이벤트가 recall에서 누락될 수 있음
- Memory type diversity가 부족
- Contextual relevance가 type에 의해 가려짐

**Proposed Solutions**:

**A) Type-Aware Diversity Boosting**
```csharp
// Ensure diversity across memory types
var selected = new List<Memory>();
foreach (var type in new[] { MemoryType.Procedural, MemoryType.Semantic, MemoryType.Episodic })
{
    var topN = memories
        .Where(m => m.Type == type)
        .OrderByDescending(m => m.Score)
        .Take(limit / 3);
    selected.AddRange(topN);
}
```

**B) Type-Specific Score Weights**
```csharp
// Different weight distributions per type
switch (memory.Type)
{
    case MemoryType.Procedural:
        // Importance-heavy, recency-light
        score = 0.5*importance + 0.3*semantic + 0.1*recency + 0.1*access;
        break;
    case MemoryType.Semantic:
        // Balanced
        score = 0.3*importance + 0.4*semantic + 0.2*recency + 0.1*access;
        break;
    case MemoryType.Episodic:
        // Recency-heavy, importance-light
        score = 0.1*importance + 0.3*semantic + 0.5*recency + 0.1*access;
        break;
}
```

---

## 2. Deduplication Effectiveness Issues

### Issue 2.1: Deduplication Target Not Met
**Observation from game stats**:
```
Expected (no dedup):   ~86 memories
Expected (with dedup): ~56 memories (34% reduction target)
Actual:                136 memories (-58.1% increase!)
```

**Problem**:
- Deduplication이 작동하지 않음
- 오히려 메모리가 예상보다 58% 더 많이 생성됨
- Phase 20 목표(34% 감소) 실패

**Root Cause Analysis**:

**Hypothesis 1: Semantic Similarity Threshold Too High**
```csharp
// Current threshold: 0.80
// Similar content but threshold not met:

Content A: "[MY_QUESTION_R1] I asked: Is it a living thing?"
Content B: "[QA_R1] Q: Is it a living thing? -> A: No"

Similarity: ~0.75 (below 0.80 threshold)
→ Not deduplicated despite containing same question
```

**Hypothesis 2: ContentType Metadata Not Working**
```csharp
// Logs show both memories exist:
[1.17] [QA_R1] Q: Is it a living thing? -> A: No
[1.10] [MY_QUESTION_R1] I asked: Is it a living thing?

// Expected: Deduplication should merge these
// Actual: Both stored separately
```

**Hypothesis 3: Deduplication Only Runs on Store, Not Batch**
```csharp
// If storing 4 memories in quick succession:
await StoreAsync(ROUND);      // Check dedup against existing
await StoreAsync(MY_QUESTION); // Check dedup against existing
await StoreAsync(QA);          // Check dedup against existing
await StoreAsync(DEDUCTION);   // Check dedup against existing

// Problem: They don't check against each other in the batch
// All 4 get stored even if similar
```

**Impact**:
- Memory growth rate too high
- Storage waste
- Context size inflation
- Recall performance degradation at scale

**Proposed Solutions**:

**A) Lower Similarity Threshold**
```csharp
// Current: 0.80
// Proposed: Tiered thresholds
Exact duplicate: 0.95
High similarity: 0.85
Medium similarity: 0.75
Low similarity: 0.65

// Action per tier:
0.95+: Delete duplicate, keep original
0.85-0.94: Merge content, boost score
0.75-0.84: Update original, add metadata
0.65-0.74: Keep both, add "related" link
```

**B) Batch Deduplication**
```csharp
// Before storing multiple memories, check within batch
public async Task StoreBatchAsync(IEnumerable<Memory> memories)
{
    var deduplicated = new List<Memory>();
    foreach (var mem in memories)
    {
        var isDuplicate = deduplicated.Any(m =>
            Similarity(m.Content, mem.Content) > threshold);

        if (!isDuplicate)
            deduplicated.Add(mem);
    }

    // Then check against existing DB
    await StoreAsync(deduplicated);
}
```

**C) ContentType-Aware Deduplication**
```csharp
// Different thresholds per content type
QUESTION: 0.90 (questions are short, need high threshold)
ANSWER: 0.85
DEDUCTION: 0.75 (longer content, can be more lenient)
ROUND: 1.00 (exact match only)
```

---

### Issue 2.2: Memory Growth Rate Analysis
**Observation from logs**:
```
Round 1: 5 memories (initial)
Round 2: 9 memories (+4)
Round 3: 13 memories (+4)
Round 4-20: ~4 memories/round

Total after 20 rounds: 136 memories
Expected per round: 4
Actual per round: 136/20 = 6.8 memories/round
```

**Problem**:
- 실제로 라운드당 6-8개 메모리 생성
- 예상(4개)보다 70% 더 많음

**Detailed Memory Creation Pattern**:
```
Per round storage calls:
1. ROUND memory (both players)        → 2 memories
2. MY_QUESTION (Beta only)             → 1 memory
3. QUESTION (Alpha's view)             → 1 memory
4. ANSWER (Alpha only)                 → 1 memory
5. QA pair (Beta's view)               → 1 memory
6. DEDUCTION (Beta only)               → 1 memory

Subtotal: 7 memories/round (not 4!)

Plus:
- Duplicate ROUND tracking (old rounds not cleaned)
- Multiple QA representations
```

**Impact**:
- Faster context growth than expected
- 15-memory limit reached too early
- Important older memories evicted prematurely

**Proposed Solutions**:

**A) Memory Lifecycle Management**
```csharp
// Auto-cleanup old episodic memories
public class MemoryLifecyclePolicy
{
    public TimeSpan EpisodicTTL { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SemanticTTL { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan ProceduralTTL { get; set; } = TimeSpan.MaxValue; // Never expire

    public async Task CleanupExpiredAsync()
    {
        // Remove expired episodic memories beyond limit
        var expired = memories
            .Where(m => m.Type == Episodic &&
                        DateTime.Now - m.LastAccessed > EpisodicTTL)
            .OrderBy(m => m.Importance);

        await DeleteAsync(expired);
    }
}
```

**B) Smart Memory Consolidation**
```csharp
// Merge similar episodic memories periodically
public async Task ConsolidateEpisodicAsync()
{
    var episodes = memories
        .Where(m => m.Type == Episodic)
        .GroupBy(m => m.CreatedAt.Date); // Group by day

    foreach (var group in episodes)
    {
        if (group.Count() > 10)
        {
            // Summarize into single memory
            var summary = await SummarizeAsync(group);
            await StoreAsync(summary);
            await DeleteAsync(group);
        }
    }
}
```

**C) Single Source of Truth**
```csharp
// Avoid storing same information multiple ways
// Instead of: MY_QUESTION + QA + QUESTION
// Store only: QA with metadata

public class QAPair
{
    public string Question { get; set; }
    public string Answer { get; set; }
    public string Asker { get; set; }
    public string Responder { get; set; }
    public DateTime Timestamp { get; set; }

    // Both players can recall same QAPair with different perspectives
}
```

---

## 3. Memory Type Classification Issues

### Issue 3.1: Type Distribution Imbalance
**Observation from game stats**:
```
Beta (80 memories):
- Episodic: 59 (73.8%)
- Semantic: 17 (21.2%)
- Procedural: 4 (5.0%)

Alpha (56 memories):
- Episodic: 54 (96.4%)
- Procedural: 1 (1.8%)
- Semantic: 1 (1.8%)
```

**Problem**:
- Episodic memories dominate (73-96%)
- Procedural memories only 2-5% (too few)
- Alpha has almost no semantic/procedural memories

**Impact**:
- Episodic memories are noisy and decay fast
- Important procedural knowledge gets buried
- Long-term learning is not captured

**Proposed Solutions**:

**A) Type-Based Capacity Limits**
```csharp
public class TypedCapacityManager
{
    public int ProceduralCapacity { get; set; } = 10;  // 10/15 = 66% reserved
    public int SemanticCapacity { get; set; } = 5;      // 5/15 = 33% reserved
    public int EpisodicCapacity { get; set; } = 15;     // Flexible, fills remaining

    public bool CanStore(MemoryType type)
    {
        var current = memories.Count(m => m.Type == type);
        return type switch
        {
            Procedural => current < ProceduralCapacity,
            Semantic => current < SemanticCapacity,
            Episodic => memories.Count < TotalCapacity, // Use remainder
            _ => false
        };
    }
}
```

**B) Auto-Promotion from Episodic to Semantic**
```csharp
// When episodic pattern repeats, promote to semantic
public async Task DetectPatternsAsync()
{
    var episodicGroups = memories
        .Where(m => m.Type == Episodic)
        .GroupBy(m => ExtractPattern(m.Content));

    foreach (var group in episodicGroups)
    {
        if (group.Count() >= 3) // Pattern confirmed
        {
            var semantic = new Memory
            {
                Type = MemoryType.Semantic,
                Content = $"Pattern: {group.Key}",
                Importance = group.Average(m => m.Importance) * 1.2f,
                ConfirmationCount = group.Count()
            };

            await StoreAsync(semantic);
            // Keep episodic for history, but lower priority
        }
    }
}
```

**C) Procedural Memory Extraction**
```csharp
// Extract procedures from repeated episodic sequences
public async Task ExtractProceduresAsync()
{
    var sequences = memories
        .Where(m => m.Type == Episodic)
        .OrderBy(m => m.CreatedAt)
        .Window(3); // Look for 3-step sequences

    var repeatedSequences = sequences
        .GroupBy(s => s.Select(m => m.ActionType))
        .Where(g => g.Count() >= 2); // Repeated at least twice

    foreach (var seq in repeatedSequences)
    {
        var procedure = new Memory
        {
            Type = MemoryType.Procedural,
            Content = $"Procedure: {string.Join(" → ", seq.Key)}",
            Importance = 0.9f
        };
        await StoreAsync(procedure);
    }
}
```

---

## 4. Recall Performance & Caching Issues

### Issue 4.1: Cache Hit Rate Analysis
**Observation from logs**:
```
Round 1:
[BETA] Recall: 754ms
[ALPHA] Recall: 314ms

Round 12:
[BETA] Recall: 77ms  ← 90% faster!
[ALPHA] Recall: 309ms

Alpha Round 10:
[ALPHA] Recalled 15 memories (⏱️ 3ms)  ← Cache hit!

Alpha Round 12:
[ALPHA] Recalled 15 memories (⏱️ 0ms)  ← Cache hit!
```

**Analysis**:
- Some recalls are 0-3ms (cache hits)
- Others are 300-700ms (DB queries)
- Cache is working but inconsistently

**Problem**:
- Cache strategy not documented
- Cache key generation unclear
- Cache invalidation policy unknown
- No metrics on cache hit rate

**Impact**:
- Performance unpredictable
- Cannot optimize cache strategy
- Difficult to debug slow recalls

**Proposed Solutions**:

**A) Cache Metrics & Monitoring**
```csharp
public class RecallMetrics
{
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public double CacheHitRate => (double)CacheHits / (CacheHits + CacheMisses);

    public TimeSpan AvgCacheHitLatency { get; set; }
    public TimeSpan AvgCacheMissLatency { get; set; }

    public void RecordRecall(bool cacheHit, TimeSpan latency)
    {
        if (cacheHit)
        {
            CacheHits++;
            AvgCacheHitLatency = UpdateAverage(AvgCacheHitLatency, latency, CacheHits);
        }
        else
        {
            CacheMisses++;
            AvgCacheMissLatency = UpdateAverage(AvgCacheMissLatency, latency, CacheMisses);
        }
    }
}
```

**B) Smart Cache Key Generation**
```csharp
// Current (assumed): Hash of query string
// Problem: Similar queries don't share cache

// Proposed: Semantic cache key
public string GenerateCacheKey(string query, int limit)
{
    var embedding = await EmbedAsync(query);
    var embeddingHash = HashEmbedding(embedding, precision: 0.1); // Round to 0.1
    return $"{userId}:{embeddingHash}:{limit}";
}

// Similar queries (within 0.1 cosine distance) share cache
// "what is the secret" and "tell me the secret" → same cache key
```

**C) Cache Warming Strategy**
```csharp
// Pre-populate cache for common queries
public async Task WarmCacheAsync(string userId)
{
    var commonQueries = new[]
    {
        "recent events",
        "important facts",
        "rules and procedures",
        "confirmed information",
        "recent questions and answers"
    };

    foreach (var query in commonQueries)
    {
        await RecallAsync(userId, query); // Populate cache
    }
}
```

---

### Issue 4.2: Recall Latency Variance
**Observation**:
```
Beta Recall Times (20 rounds):
Min: 77ms (Round 12)
Max: 867ms (Round 5)
Avg: 410ms
StdDev: ~200ms (high variance!)

Distribution:
< 100ms: 1 round (5%)
100-300ms: 6 rounds (30%)
300-500ms: 8 rounds (40%)
500-700ms: 4 rounds (20%)
700+ms: 1 round (5%)
```

**Problem**:
- 10x variance between fastest and slowest
- High standard deviation (200ms)
- Unpredictable performance

**Root Cause Analysis**:
```
Fast recalls (< 100ms):
- Cache hit
- Few memories in DB
- Simple query

Slow recalls (> 700ms):
- Cache miss
- Many memories to scan
- Complex query with multiple terms
- Vector similarity computation expensive
```

**Impact**:
- Poor user experience (unpredictable latency)
- Cannot guarantee SLA
- Difficult to scale

**Proposed Solutions**:

**A) Query Complexity Analysis**
```csharp
public class QueryComplexityAnalyzer
{
    public int EstimateComplexity(string query)
    {
        int complexity = 0;

        // More terms = more complex
        complexity += query.Split(' ').Length;

        // Special keywords increase complexity
        if (query.Contains("similar")) complexity += 5;
        if (query.Contains("related")) complexity += 5;

        // Question marks suggest broad search
        complexity += query.Count(c => c == '?') * 2;

        return complexity;
    }

    public int AdjustLimit(int requestedLimit, int complexity)
    {
        // Reduce limit for complex queries to maintain latency
        if (complexity > 20) return Math.Min(requestedLimit, 5);
        if (complexity > 10) return Math.Min(requestedLimit, 10);
        return requestedLimit;
    }
}
```

**B) Progressive Recall**
```csharp
// Return fast results first, then refine
public async IAsyncEnumerable<Memory> RecallStreamAsync(string userId, string query)
{
    // Stage 1: Cache check (< 10ms)
    var cached = GetFromCache(userId, query);
    if (cached != null)
    {
        foreach (var mem in cached)
            yield return mem;
        yield break;
    }

    // Stage 2: Exact keyword match (< 50ms)
    var exactMatches = await GetExactMatchesAsync(userId, query);
    foreach (var mem in exactMatches.Take(5))
        yield return mem;

    // Stage 3: Semantic search (< 300ms)
    var semanticMatches = await GetSemanticMatchesAsync(userId, query);
    foreach (var mem in semanticMatches.Take(10))
        if (!exactMatches.Contains(mem))
            yield return mem;
}
```

**C) Recall Budget Management**
```csharp
public class RecallBudget
{
    public TimeSpan MaxLatency { get; set; } = TimeSpan.FromMilliseconds(500);

    public async Task<List<Memory>> RecallWithBudgetAsync(
        string userId, string query, int limit)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<Memory>();

        // Tier 1: Cache (budget: 10ms)
        results.AddRange(await GetCachedAsync(userId, query));
        if (sw.Elapsed > TimeSpan.FromMilliseconds(10) || results.Count >= limit)
            return results.Take(limit).ToList();

        // Tier 2: Keyword index (budget: 100ms)
        results.AddRange(await GetKeywordMatchesAsync(userId, query));
        if (sw.Elapsed > TimeSpan.FromMilliseconds(100) || results.Count >= limit)
            return results.Take(limit).ToList();

        // Tier 3: Vector search (budget: remaining time)
        var remaining = MaxLatency - sw.Elapsed;
        results.AddRange(await GetVectorMatchesAsync(userId, query, remaining));

        return results.Take(limit).ToList();
    }
}
```

---

## 5. Context Size & Token Management Issues

### Issue 5.1: Context Growth Pattern
**Observation from logs**:
```
Round 1:
Beta context: 1,036 chars
Alpha context: 358 chars

Round 10:
Beta context: 2,052 chars (+98%)
Alpha context: 1,423 chars (+297%)

Round 20:
Beta context: 1,957 chars (stable)
Alpha context: 1,723 chars (stable)
```

**Problem**:
- Context doubles in first 10 rounds
- Alpha context grows 3x faster than Beta
- Stabilizes only after hitting 15-memory limit

**Root Cause**:
```
Memory limit: 15 memories
Avg memory size: ~100-150 chars

Expected context: 15 * 125 = 1,875 chars
Actual context: 1,700-2,100 chars (matches)

Growth pattern:
- Rounds 1-4: Linear growth (adding memories)
- Rounds 5-20: Stable (limit reached, eviction starts)
```

**Impact**:
- LLM token costs increase 2-3x
- Prompt size unpredictable in early rounds
- Need to over-provision token budget

**Proposed Solutions**:

**A) Context Budget Manager**
```csharp
public class ContextBudgetManager
{
    public int MaxTokens { get; set; } = 2000;
    public int AvgCharsPerToken { get; set; } = 4; // English average

    public int MaxChars => MaxTokens * AvgCharsPerToken; // 8000 chars

    public async Task<string> BuildContextAsync(
        List<Memory> memories, int maxChars)
    {
        var context = new StringBuilder();
        int currentChars = 0;

        foreach (var mem in memories.OrderByDescending(m => m.Score))
        {
            var memText = FormatMemory(mem);
            if (currentChars + memText.Length > maxChars)
            {
                // Truncate last memory or skip
                var remaining = maxChars - currentChars;
                if (remaining > 50) // Worth including partial
                    context.Append(memText[..remaining] + "...");
                break;
            }

            context.AppendLine(memText);
            currentChars += memText.Length;
        }

        return context.ToString();
    }
}
```

**B) Adaptive Memory Limit**
```csharp
// Adjust limit based on memory sizes
public int CalculateOptimalLimit(int targetTokens)
{
    var avgMemorySize = memories.Average(m => m.Content.Length);
    var avgMemoryTokens = avgMemorySize / 4;

    var optimalLimit = targetTokens / avgMemoryTokens;
    return Math.Clamp(optimalLimit, 5, 20); // Min 5, max 20
}

// Example:
// Target: 2000 tokens
// Avg memory: 100 chars = 25 tokens
// Optimal limit: 2000 / 25 = 80 memories (clamped to 20)
```

**C) Memory Compression**
```csharp
// Compress old memories to save tokens
public async Task<Memory> CompressAsync(Memory memory)
{
    if (memory.AccessCount < 2) return memory; // Don't compress new

    var compressed = await LLM.SummarizeAsync(
        memory.Content,
        maxLength: 50); // Compress to 50 chars

    return memory with
    {
        Content = compressed,
        Metadata = memory.Metadata.Add("original_length", memory.Content.Length)
    };
}
```

---

## 6. Query Intent & Relevance Issues

### Issue 6.1: Generic Query vs Specific Query
**Observation from logs**:
```
Round 1 Beta query:
"strategy rules game previous"
→ Returns: GAME_RULES (1.69), STRATEGY (1.48), DEDUCTION_TEMPLATE (1.43)
✅ Good: Strategic memories top-ranked

Round 10 Beta query:
"secret rules previous questions answers Is it commonly found in a household?"
→ Returns: Same strategic memories still top
⚠️ Problem: Specific question not prioritized
```

**Problem**:
- Generic queries work well
- Specific queries don't boost relevant content enough
- Long queries with specific terms don't outrank generic high-importance memories

**Root Cause**:
```
HybridScore formula:
α·Semantic + β·Recency + γ·Importance + δ·AccessFrequency + KeywordBoost

Problem:
- Importance (γ) is fixed at 0.9-1.0 for strategic memories
- Semantic similarity (α) ranges 0.0-1.0 based on query
- Even perfect semantic match (1.0) can't beat high importance (0.95)

Example:
Strategic memory: 0.3*semantic + 0.9*importance = 0.3 + 0.9 = 1.2
Specific memory: 1.0*semantic + 0.5*importance = 1.0 + 0.5 = 1.5
→ Specific wins, but gap is small
```

**Impact**:
- Specific, relevant memories buried under generic important ones
- Query intent not captured
- User looking for specific fact gets generic context

**Proposed Solutions**:

**A) Query Intent Classification**
```csharp
public enum QueryIntent
{
    Factual,      // "what is X", "who did Y"
    Contextual,   // "recent events", "current status"
    Temporal,     // "yesterday", "last week"
    Procedural,   // "how to", "steps for"
    Relational    // "similar to", "related to"
}

public QueryIntent ClassifyIntent(string query)
{
    if (Regex.IsMatch(query, @"\b(what|who|where|when)\b", RegexOptions.IgnoreCase))
        return QueryIntent.Factual;

    if (Regex.IsMatch(query, @"\b(recent|current|latest|now)\b", RegexOptions.IgnoreCase))
        return QueryIntent.Contextual;

    if (Regex.IsMatch(query, @"\b(yesterday|last|ago|before)\b", RegexOptions.IgnoreCase))
        return QueryIntent.Temporal;

    if (Regex.IsMatch(query, @"\b(how|steps|procedure|process)\b", RegexOptions.IgnoreCase))
        return QueryIntent.Procedural;

    return QueryIntent.Relational;
}

public float[] GetWeightsForIntent(QueryIntent intent)
{
    return intent switch
    {
        QueryIntent.Factual => new[] { 0.6f, 0.1f, 0.2f, 0.1f }, // Boost semantic
        QueryIntent.Contextual => new[] { 0.3f, 0.4f, 0.2f, 0.1f }, // Boost recency
        QueryIntent.Temporal => new[] { 0.2f, 0.6f, 0.1f, 0.1f }, // Heavy recency
        QueryIntent.Procedural => new[] { 0.3f, 0.1f, 0.5f, 0.1f }, // Boost importance
        QueryIntent.Relational => new[] { 0.5f, 0.2f, 0.2f, 0.1f }, // Boost semantic
        _ => new[] { 0.25f, 0.25f, 0.25f, 0.25f } // Balanced
    };
}
```

**B) Query Specificity Score**
```csharp
public float CalculateQuerySpecificity(string query)
{
    float specificity = 0f;

    // Longer queries are more specific
    var wordCount = query.Split(' ').Length;
    specificity += Math.Min(wordCount / 10f, 0.3f); // Max 0.3

    // Rare words indicate specificity
    var rareWordCount = query.Split(' ')
        .Count(w => IsRareWord(w, threshold: 0.01)); // < 1% frequency
    specificity += rareWordCount * 0.1f; // +0.1 per rare word

    // Questions are more specific
    if (query.Contains('?'))
        specificity += 0.2f;

    return Math.Clamp(specificity, 0f, 1f);
}

// Use specificity to boost semantic weight
public float AdjustSemanticWeight(float baseWeight, float specificity)
{
    return baseWeight * (1 + specificity); // Up to 2x boost
}
```

**C) Dynamic Importance Damping**
```csharp
// Reduce importance weight when query is very specific
public float DampImportance(Memory memory, string query, float specificity)
{
    var baseImportance = memory.Importance;

    if (specificity > 0.7f) // Very specific query
    {
        // Reduce importance of non-matching memories
        var semanticMatch = CosineSimilarity(memory.Embedding, QueryEmbedding(query));
        if (semanticMatch < 0.5f)
        {
            return baseImportance * 0.5f; // Halve importance
        }
    }

    return baseImportance;
}
```

---

## 7. Observability & Debugging Issues

### Issue 7.1: Limited Recall Introspection
**Current Logs**:
```
[BETA] Recalled 13 memories (⏱️ 264ms, 📝 1,758 chars):
       [1.69] [GAME_RULES] I am Beta...
       [1.47] [STRATEGY_PHASE1] Rounds 1-3...
```

**Missing Information**:
- Why did each memory get that specific score?
- What are the individual score components (semantic, recency, importance)?
- Which memories were candidates but didn't make the cut?
- Cache hit or miss?
- Query processing details

**Impact**:
- Cannot debug poor recall results
- Cannot tune scoring weights effectively
- Cannot understand why important memory was missed

**Proposed Solutions**:

**A) Detailed Score Breakdown**
```csharp
public class ScoredMemory
{
    public Memory Memory { get; set; }
    public float TotalScore { get; set; }

    // Score components
    public float SemanticScore { get; set; }
    public float RecencyScore { get; set; }
    public float ImportanceScore { get; set; }
    public float AccessFrequencyScore { get; set; }
    public float KeywordBoost { get; set; }
    public float MetadataBoost { get; set; }

    public string Explain()
    {
        return $"Total: {TotalScore:F2} = " +
               $"Semantic({SemanticScore:F2}) + " +
               $"Recency({RecencyScore:F2}) + " +
               $"Importance({ImportanceScore:F2}) + " +
               $"Access({AccessFrequencyScore:F2}) + " +
               $"Keyword({KeywordBoost:F2}) + " +
               $"Metadata({MetadataBoost:F2})";
    }
}
```

**B) Recall Audit Trail**
```csharp
public class RecallAudit
{
    public string UserId { get; set; }
    public string Query { get; set; }
    public DateTime Timestamp { get; set; }

    public int TotalCandidates { get; set; }
    public int ReturnedCount { get; set; }
    public bool CacheHit { get; set; }

    public List<ScoredMemory> TopCandidates { get; set; } // Top 20
    public List<ScoredMemory> Returned { get; set; }      // Top N returned
    public List<ScoredMemory> JustMissed { get; set; }    // N+1 to N+5

    public TimeSpan QueryProcessingTime { get; set; }
    public TimeSpan EmbeddingTime { get; set; }
    public TimeSpan SearchTime { get; set; }
    public TimeSpan ScoringTime { get; set; }
}
```

**C) Verbose Logging Mode**
```csharp
public class RecallOptions
{
    public bool VerboseLogging { get; set; }
    public bool ExplainScores { get; set; }
    public bool ShowCandidates { get; set; }
}

// Usage:
var results = await RecallAsync(userId, query, new RecallOptions
{
    VerboseLogging = true,
    ExplainScores = true,
    ShowCandidates = true
});

// Output:
// [RECALL] Query: "is it a plant"
// [RECALL] Candidates: 80 memories
// [RECALL] Cache: MISS
// [RECALL] Embedding: 45ms
// [RECALL] Search: 150ms
// [RECALL] Scoring: 69ms
// [RECALL] Top candidate: [1.69] GAME_RULES
//   - Semantic: 0.35 (cosine=0.35)
//   - Recency: 0.20 (age=15min, bias=0.5)
//   - Importance: 0.95 (stored)
//   - Access: 0.15 (count=20)
//   - Keyword: 0.04 (0/5 matched)
//   - Total: 1.69
// [RECALL] Returned: 13 memories (limit=15)
// [RECALL] Just missed: [1.09] MY_QUESTION_R0
```

---

## Summary: Priority Ranking

### 🔴 Critical (Blocking Production)
1. **Deduplication Fix** (Issue 2.1)
   - Current: -58% increase vs 34% reduction target
   - Impact: Memory explosion, storage waste

2. **Score Distribution** (Issue 1.1)
   - Current: Narrow 0.59 range
   - Impact: Poor ranking, wrong memories selected

### 🟡 High (Significant Impact)
3. **Memory Growth Rate** (Issue 2.2)
   - Current: 6.8/round vs 4/round expected
   - Impact: Early limit hit, important memories evicted

4. **Recall Latency Variance** (Issue 4.2)
   - Current: 10x variance (77ms to 867ms)
   - Impact: Unpredictable performance, poor UX

5. **Query Intent** (Issue 6.1)
   - Current: Generic queries work, specific don't
   - Impact: Users don't find what they search for

### 🟢 Medium (Optimization)
6. **Type Distribution** (Issue 3.1)
   - Current: 73-96% episodic
   - Impact: Long-term learning not captured

7. **Context Growth** (Issue 5.1)
   - Current: 2-3x growth, then stable
   - Impact: Token cost unpredictable

8. **Observability** (Issue 7.1)
   - Current: Limited introspection
   - Impact: Hard to debug, hard to tune

### 🔵 Low (Nice to Have)
9. **Cache Metrics** (Issue 4.1)
   - Current: Works but unmeasured
   - Impact: Cannot optimize further

10. **Type Clustering** (Issue 1.2)
    - Current: Procedural always wins
    - Impact: Diversity issues
