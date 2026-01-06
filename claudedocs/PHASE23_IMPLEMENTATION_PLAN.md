# Phase 23 Implementation Plan

## Phase 23.1: Memory Type Distribution Balancing

### Problem Analysis

**Current Issue**: Episodic 73-96%, Procedural 2-5% (severe imbalance)

**Root Causes** (from `LocalMemoryClassifier.cs` analysis):
1. **Default is Episodic** (line 235): Any content not matching specific patterns → Episodic
2. **Limited Procedural patterns** (8 patterns): Only explicit "how to", "step by step" etc.
3. **Minimal Semantic patterns** (4 keywords): "means", "definition", "concept", "principle"
4. **Missing implicit procedural knowledge**: Tool usage, environment configuration

### Cognitive Psychology Foundation

Based on research ([tutor2u](https://www.tutor2u.net/psychology/reference/episodic-procedural-and-semantic-memory), [Social Sci LibreTexts](https://socialsci.libretexts.org/Bookshelves/Psychology/Cognitive_Psychology/Cognitive_Psychology_(Andrade_and_Walker)/05:_Working_Memory/5.03:_Long_Term_Memory)):

| Type | Definition | Examples |
|------|------------|----------|
| **Episodic** | Personal experiences at specific times/places | "Yesterday I fixed the API error", "party on 7th birthday" |
| **Semantic** | General knowledge, context-free facts | "Python is a language", "JWT for authentication" |
| **Procedural** | Implicit "how to" knowledge, skills | "How to configure Docker", "I always use pnpm" |

**Key Insight**: Procedural memory includes both:
- Explicit procedures: "First, install Docker. Then, run..."
- Implicit habitual practices: "I use pnpm", "The project is built with React"

### Design: Multi-Label Classification System

#### 1. Model Changes

```csharp
// Extend MemoryClassification
public sealed class MemoryClassification
{
    // Existing fields...
    public required MemoryType Type { get; init; }  // Primary type

    // NEW: Multi-label support
    public IReadOnlyList<MemoryType> SecondaryTypes { get; init; } = [];

    // NEW: Type confidence scores (0.0-1.0)
    public IReadOnlyDictionary<MemoryType, float> TypeConfidences { get; init; } =
        new Dictionary<MemoryType, float>();
}
```

#### 2. Enhanced Pattern Detection

**Procedural Indicators** (expand from 8 to 30+):
```csharp
private static readonly string[] ProceduralIndicators =
[
    // Explicit procedures
    "how to", "step by step", "first,", "then,", "finally,",
    "to do this", "you need to", "make sure to", "don't forget to",

    // Tool/framework usage (NEW)
    "use", "uses", "using", "built with", "configured with",
    "based on", "running on", "powered by", "depends on",

    // Environment/setup (NEW)
    "installed", "set up", "deploy with", "package with",
    "initialize", "configure", "install", "setup",

    // Habitual patterns (NEW)
    "always", "usually", "typically", "generally", "normally",
    "prefer to", "tend to", "habit of", "practice of"
];
```

**Semantic Indicators** (expand from 4 to 20+):
```csharp
private static readonly string[] SemanticIndicators =
[
    // Existing
    "means", "definition", "concept", "principle",

    // Knowledge/facts (NEW)
    "is a", "refers to", "defined as", "known as",
    "type of", "kind of", "category of", "class of",
    "generally", "typically", "usually", "commonly",

    // Explanations (NEW)
    "because", "therefore", "thus", "hence", "consequently",
    "reason", "cause", "effect"
];
```

**Episodic Indicators** (NEW - explicit markers):
```csharp
private static readonly string[] EpisodicIndicators =
[
    // Time markers
    "yesterday", "today", "tomorrow", "last week", "next month",
    "ago", "recently", "previously", "earlier", "later",
    "on monday", "at 3pm", "in january",

    // Personal events
    "i did", "we went", "i saw", "i met", "i talked",
    "happened", "occurred", "took place", "experienced",

    // Location markers
    "at the", "in the", "where", "there", "here"
];
```

#### 3. Multi-Score Classification Algorithm

```csharp
private MemoryClassification ClassifyWithMultiLabel(string content)
{
    var lower = content.ToLowerInvariant();

    // Calculate score for each type
    var scores = new Dictionary<MemoryType, float>
    {
        [MemoryType.Episodic] = CalculateEpisodicScore(lower),
        [MemoryType.Semantic] = CalculateSemanticScore(lower),
        [MemoryType.Procedural] = CalculateProceduralScore(lower),
        [MemoryType.Fact] = CalculateFactScore(lower)
    };

    // Primary type = highest score
    var primaryType = scores.OrderByDescending(x => x.Value).First().Key;

    // Secondary types = scores >= 0.3 (excluding primary)
    var secondaryTypes = scores
        .Where(x => x.Key != primaryType && x.Value >= 0.3f)
        .Select(x => x.Key)
        .ToList();

    return new MemoryClassification
    {
        Type = primaryType,
        SecondaryTypes = secondaryTypes,
        TypeConfidences = scores,
        // ... other fields
    };
}

private float CalculateEpisodicScore(string lower)
{
    float score = 0.2f; // Base score

    // Time/location markers (+0.3 each, max 2)
    int markerCount = EpisodicIndicators.Count(i => lower.Contains(i));
    score += Math.Min(markerCount * 0.3f, 0.6f);

    // Personal pronouns in past tense (+0.2)
    if ((lower.Contains("i ") || lower.Contains("we ")) &&
        (lower.Contains("did") || lower.Contains("was") || lower.Contains("were")))
    {
        score += 0.2f;
    }

    return Math.Clamp(score, 0f, 1f);
}

private float CalculateSemanticScore(string lower)
{
    float score = 0.1f;

    // Semantic indicators (+0.25 each, max 3)
    int count = SemanticIndicators.Count(i => lower.Contains(i));
    score += Math.Min(count * 0.25f, 0.75f);

    // Definition pattern: "X is a Y" (+0.15)
    if (Regex.IsMatch(lower, @"\b\w+ is a \w+"))
    {
        score += 0.15f;
    }

    return Math.Clamp(score, 0f, 1f);
}

private float CalculateProceduralScore(string lower)
{
    float score = 0.1f;

    // Procedural indicators (+0.2 each, max 4)
    int count = ProceduralIndicators.Count(i => lower.Contains(i));
    score += Math.Min(count * 0.2f, 0.8f);

    // Tool names (React, Docker, pnpm, etc.) (+0.1)
    if (ToolKeywords.Keys.Any(k => lower.Contains(k)))
    {
        score += 0.1f;
    }

    return Math.Clamp(score, 0f, 1f);
}

private float CalculateFactScore(string lower)
{
    // Existing logic from FactIndicators
    // "my name is", "i am", "i prefer", etc.
    int count = FactIndicators.Count(i => lower.Contains(i));
    return count > 0 ? Math.Clamp(0.6f + count * 0.1f, 0f, 1f) : 0.1f;
}
```

#### 4. Type-Aware Adaptive Weighting

```csharp
// NEW interface
public interface IMemoryTypeBalancer
{
    /// <summary>
    /// Calculate boost factor for underrepresented memory types.
    /// </summary>
    float GetTypeBoost(MemoryType type, string userId);

    /// <summary>
    /// Get current type distribution for user.
    /// </summary>
    Task<Dictionary<MemoryType, float>> GetTypeDistributionAsync(string userId);
}

// Implementation
public sealed class MemoryTypeBalancer : IMemoryTypeBalancer
{
    private readonly IMemoryStore _store;
    private readonly TypeBalancerOptions _options;

    public float GetTypeBoost(MemoryType type, string userId)
    {
        var distribution = GetTypeDistribution(userId);
        var currentPercentage = distribution.GetValueOrDefault(type, 0f);
        var targetPercentage = _options.TargetDistribution.GetValueOrDefault(type, 0.25f);

        // Boost = (target - current) * sensitivity
        // If current=0.05, target=0.20, boost = 0.15 * 2.0 = 0.3
        var boost = (targetPercentage - currentPercentage) * _options.BoostSensitivity;

        return Math.Clamp(boost, 0f, 0.5f); // Max 50% boost
    }
}

// Configuration
public sealed class TypeBalancerOptions
{
    public Dictionary<MemoryType, float> TargetDistribution { get; set; } = new()
    {
        [MemoryType.Episodic] = 0.40f,    // 40% target
        [MemoryType.Semantic] = 0.30f,    // 30% target
        [MemoryType.Procedural] = 0.20f,  // 20% target
        [MemoryType.Fact] = 0.10f         // 10% target
    };

    public float BoostSensitivity { get; set; } = 2.0f;
    public bool Enabled { get; set; } = true;
}
```

#### 5. Integration with Scoring

```csharp
// In DefaultScoringService.CalculateHybridScore()
public float CalculateHybridScore(MemoryUnit memory, string query, ...)
{
    var baseScore = /* existing calculation */;

    // NEW: Type-aware boost
    if (_typeBalancer != null && _options.TypeBalancingEnabled)
    {
        var typeBoost = _typeBalancer.GetTypeBoost(memory.Type, memory.UserId);
        baseScore += typeBoost;
    }

    return baseScore;
}
```

### Expected Impact

**Type Distribution**:
- Episodic: 73-96% → ~40% (56% reduction)
- Semantic: Implicit → ~30% (explicit targeting)
- Procedural: 2-5% → ~20% (4-10x improvement)
- Fact: Implicit → ~10%

**Classification Quality**:
- Multi-label support captures nuanced content
- Explicit tool/framework usage → Procedural
- Habitual patterns → Procedural + Fact
- General knowledge → Semantic

### Test Coverage

```csharp
// Phase 23.1 Tests (20+ tests)
public class EnhancedMemoryClassifierTests
{
    [Theory]
    [InlineData("I use pnpm for package management", MemoryType.Procedural)]
    [InlineData("The project is built with React", MemoryType.Procedural)]
    [InlineData("Docker is a containerization platform", MemoryType.Semantic)]
    [InlineData("Yesterday I debugged the API error", MemoryType.Episodic)]
    public async Task ClassifyAsync_CorrectPrimaryType(string content, MemoryType expected);

    [Fact]
    public async Task ClassifyAsync_MultiLabel_CapturesBothTypes()
    {
        // "I always use TypeScript for my projects"
        // → Primary: Procedural, Secondary: Fact
    }

    [Fact]
    public async Task TypeBalancer_BoostsUnderrepresentedTypes();

    [Fact]
    public async Task GetTypeDistribution_ReturnsAccuratePercentages();
}
```

### Implementation Files

1. **Models**:
   - `src/MemoryIndexer/Interfaces/IMemoryClassifier.cs` (extend MemoryClassification)
   - `src/MemoryIndexer/Interfaces/IMemoryTypeBalancer.cs` (new)
   - `src/MemoryIndexer/Configuration/MemoryIndexerOptions.cs` (add TypeBalancerOptions)

2. **Implementation**:
   - `src/MemoryIndexer.Sdk/Intelligence/Classification/LocalMemoryClassifier.cs` (enhance)
   - `src/MemoryIndexer.Sdk/Intelligence/Classification/MemoryTypeBalancer.cs` (new)

3. **Tests**:
   - `tests/MemoryIndexer.Sdk.Tests/Intelligence/Classification/EnhancedMemoryClassifierTests.cs` (new)
   - `tests/MemoryIndexer.Sdk.Tests/Intelligence/Classification/MemoryTypeBalancerTests.cs` (new)

---

## Phase 23.2: Context Growth Pattern Optimization

### Problem Analysis

**Current Issue**: 2-3x growth in first 10 rounds, then stable

**Goals**:
- Predictable context growth rate
- Configurable growth limits per tier
- Progressive summarization at boundaries

### Design: Context Growth Monitor

```csharp
public interface IContextGrowthMonitor
{
    /// <summary>
    /// Track context size over time.
    /// </summary>
    Task RecordContextSizeAsync(string userId, int tokenCount, string tier);

    /// <summary>
    /// Get growth rate statistics.
    /// </summary>
    Task<ContextGrowthStats> GetGrowthStatsAsync(string userId);

    /// <summary>
    /// Check if context growth exceeds limits.
    /// </summary>
    Task<bool> ShouldCompressAsync(string userId, string tier);
}

public sealed class ContextGrowthStats
{
    public float GrowthRate { get; init; }        // Tokens per round
    public int CurrentSize { get; init; }         // Current total tokens
    public int MaxSize { get; init; }             // Configured limit
    public float UtilizationRate { get; init; }   // Current / Max
}
```

### Implementation Approach

1. **Growth Tracking**: Record token counts per tier over time
2. **Adaptive Limits**: Reduce Working Memory capacity under high growth
3. **Progressive Summarization**: Trigger summarization at 80% utilization

---

## Phase 23.3: Observability & Debugging Enhancement

### Problem Analysis

**Current Issue**: Limited introspection into recall decisions

### Design: Recall Decision Tracing

```csharp
public interface IRecallExplainer
{
    /// <summary>
    /// Explain why specific memories were recalled.
    /// </summary>
    Task<RecallExplanation> ExplainAsync(
        string query,
        IReadOnlyList<MemoryUnit> recalled);
}

public sealed class RecallExplanation
{
    public required string Query { get; init; }
    public required QueryIntent Intent { get; init; }
    public required IReadOnlyList<MemoryScoreBreakdown> Scores { get; init; }
}

public sealed class MemoryScoreBreakdown
{
    public required Guid MemoryId { get; init; }
    public required float TotalScore { get; init; }
    public required float SemanticScore { get; init; }
    public required float RecencyScore { get; init; }
    public required float ImportanceScore { get; init; }
    public required float TypeBoost { get; init; }
    public required float IntentBoost { get; init; }
    public required string Reason { get; init; }
}
```

### OpenTelemetry Metrics

```csharp
// Memory lifecycle metrics
Meter.CreateHistogram<long>("memory.promotion.latency");
Meter.CreateCounter<long>("memory.promotion.count");
Meter.CreateHistogram<float>("memory.type.distribution");

// Recall decision metrics
Meter.CreateHistogram<long>("memory.recall.latency");
Meter.CreateHistogram<int>("memory.recall.count");
Meter.CreateHistogram<float>("memory.recall.score");
```

---

## Success Criteria

### Phase 23.1
- ✅ Type distribution within 10% of targets (Episodic ~40%, Semantic ~30%, Procedural ~20%, Fact ~10%)
- ✅ Multi-label support for nuanced classification
- ✅ Procedural classification improved by 4-10x

### Phase 23.2
- ✅ Context growth rate < 2.5x in first 10 rounds
- ✅ Adaptive limits prevent unbounded growth
- ✅ Progressive summarization maintains quality

### Phase 23.3
- ✅ All recall decisions traceable with explanations
- ✅ OpenTelemetry metrics for memory lifecycle
- ✅ Score breakdown available for debugging

### Overall
- ✅ 20+ new tests for Phase 23.1
- ✅ 10+ new tests for Phase 23.2
- ✅ 10+ new tests for Phase 23.3
- ✅ All tests passing (740+ total)
