# Evaluation Framework

Standardized evaluation framework for Memory Indexer based on cognitive science principles.

## Core KPIs

Memory Indexer's evaluation framework defines four key performance indicators:

| KPI | Description | Target | Cognitive Basis |
|-----|-------------|--------|-----------------|
| **CCR** | Context Compression Ratio | <1% for NIAH | Memory consolidation |
| **Recall@K** | Top-K retrieval precision | >80% | Semantic memory access |
| **Tier Promotion Latency** | Buffer→Short→Long transition | <100ms | Working memory transfer |
| **Information Retention** | Long-term recall accuracy | >90% | Episodic memory persistence |

### Context Compression Ratio (CCR)

```
CCR = recalled_tokens / full_history_tokens
```

Measures efficiency of context management. Lower is better.

- **<1%**: Excellent - highly compressed context
- **<5%**: Good - effective compression
- **>5%**: Review retrieval strategy

### Recall@K Efficiency

```
Recall@K = relevant_in_top_k / k
```

Measures precision of semantic retrieval. Higher is better.

- **>80%**: High accuracy
- **50-80%**: Moderate - consider reranking
- **<50%**: Low - review embedding model

### Tier Promotion Latency

Measures real-time service suitability:

| Transition | Target | Description |
|------------|--------|-------------|
| Buffer → Short | <10ms | Sensory to working memory |
| Short → Long | <50ms | Working to episodic memory |
| Long → Archive | <100ms | Episodic to semantic memory |

### Information Retention Score

```
Retention = correctly_recalled / total_stored
```

Proves superiority over sliding window approaches.

## NIAH Test (Needle In A Haystack)

Tests memory system's ability to recall specific information from large contexts.

Reference: [gkamradt/LLMTest_NeedleInAHaystack](https://github.com/gkamradt/LLMTest_NeedleInAHaystack)

### Test Methodology

1. **Haystack Generation**: ~100K tokens from Project Gutenberg texts
2. **Needle Insertion**: At configurable positions (25%, 50%, 75%)
3. **Store Phase**: Store all segments as memories
4. **Recall Phase**: Query for needle using semantic search
5. **Validation**: Verify needle found with CCR < 1%

### Usage

```csharp
var runner = services.GetRequiredService<NiahTestRunner>();

var result = await runner.RunTestAsync(new NiahTestConfig
{
    Needle = "The secret code is ALPHA-BRAVO-CHARLIE",
    NeedleQuery = "secret code",
    NeedlePosition = 0.5,  // 50% into context
    TargetHaystackTokens = 100_000,
    TargetCcr = 0.01  // 1% target
});

Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"CCR: {result.Ccr:P2}");
Console.WriteLine($"Needle Found: {result.NeedleFound}");
```

### Test Suite

Run multiple positions:

```csharp
var suite = await runner.RunTestSuiteAsync(
    baseConfig,
    positions: [0.25, 0.50, 0.75]
);

Console.WriteLine($"Overall Success Rate: {suite.OverallSuccessRate:P1}");
Console.WriteLine($"Average CCR: {suite.AverageCcr:P2}");
```

## Multi-Needle Test (RULER-inspired)

Tests memory system's ability to recall multiple pieces of information simultaneously.

Reference: [RULER](https://arxiv.org/abs/2404.06654) - Multi-hop retrieval benchmark

### Test Methodology

1. **Haystack Generation**: Large context with multiple needles
2. **Multiple Needle Insertion**: At configurable positions
3. **Query Strategies**: Combined, Separate, or Sequential queries
4. **Recovery Rate**: Measure percentage of needles found

### Usage

```csharp
var runner = services.GetRequiredService<NiahTestRunner>();

var result = await runner.RunMultiNeedleTestAsync(new MultiNeedleTestConfig
{
    Needles = new[]
    {
        new NeedleInfo { Content = "Secret code is ALPHA", Query = "secret code", Position = 0.25 },
        new NeedleInfo { Content = "Meeting at 3pm", Query = "meeting time", Position = 0.50 },
        new NeedleInfo { Content = "Password is XYZ123", Query = "password", Position = 0.75 }
    },
    TargetHaystackTokens = 100_000,
    QueryStrategy = QueryStrategy.SeparateQueries
});

Console.WriteLine($"Recovery Rate: {result.RecoveryRate:P1}");
Console.WriteLine($"Needles Found: {result.NeedlesFound}/{result.TotalNeedles}");
```

### Query Strategies

| Strategy | Description | Use Case |
|----------|-------------|----------|
| `CombinedQuery` | Single query for all needles | Simple scenarios |
| `SeparateQueries` | One query per needle | Independent facts |
| `SequentialQueries` | Queries in position order | Chain of information |

## Cognitive Scenarios

### False Memory Test

Tests handling of conflicting information updates.

**Scenario**:
1. Store: "User likes apples"
2. Store: 100 intervening memories
3. Store: "User is allergic to apples"
4. Recall: "User food preferences"

**Expected Outcomes**:
- `NewerPrioritized`: System returns newer fact (preferred)
- `ContradictionDetected`: System returns both (acceptable)
- `OlderPrioritized`: System returns older fact (problematic)

```csharp
var tests = services.GetRequiredService<CognitiveScenarioTests>();

var result = await tests.RunFalseMemoryTestAsync(new FalseMemoryTestConfig
{
    InitialFact = "User likes apples",
    ConflictingFact = "User is allergic to apples",
    RecallQuery = "User food preferences",
    InterveningMemoryCount = 100
});

Console.WriteLine($"Outcome: {result.Outcome}");
```

### Cross-Session Retention Test

Tests persistence of Archive tier data across sessions.

**Scenario**:
1. Session A: Store user profile facts
2. End Session A
3. Session B: Recall user profile

**Success Criteria**: >80% retention rate

```csharp
var result = await tests.RunCrossSessionRetentionTestAsync(new CrossSessionTestConfig
{
    UserProfileFacts = new[]
    {
        "User prefers dark mode",
        "User speaks Korean",
        "User works in software development"
    },
    RecallQuery = "User preferences and profile",
    MinRetentionRate = 0.8
});

Console.WriteLine($"Retention Rate: {result.RetentionRate:P1}");
```

## Cognitive Compliance Metrics

Based on memory science principles:

### Working Memory (7±2) Compliance

Validates Short tier stays within Baddeley's working memory capacity (5-9 items).

### Healthy Tier Flow

Validates proper memory distribution:
- Buffer ≤ 2 items
- Short ≤ 9 items
- Long ≥ 1 item (for active sessions)

```csharp
var evaluator = services.GetRequiredService<IEvaluationService>();

var compliance = await evaluator.GetCognitiveComplianceAsync(userId, sessionId);

Console.WriteLine($"Working Memory Compliant: {compliance.WorkingMemoryCompliance:P0}");
Console.WriteLine($"Healthy Tier Flow: {compliance.HealthyTierFlow}");
Console.WriteLine($"Short Tier Count: {compliance.ShortTierCount}");
```

## OpenTelemetry Integration

Evaluation metrics are exported via OpenTelemetry:

| Metric | Type | Unit |
|--------|------|------|
| `memory.evaluation.ccr` | Histogram | ratio |
| `memory.evaluation.recall_at_k` | Histogram | ratio |
| `memory.evaluation.retention_score` | Histogram | ratio |
| `memory.evaluation.tier_promotion_latency` | Histogram | ms |
| `memory.evaluation.niah_tests` | Counter | tests |

## DI Registration

```csharp
services.AddMemoryIndexer(options => { /* ... */ });
services.AddMemoryEvaluation();  // Registers evaluation services
```

## Evaluation Report

Generate comprehensive reports:

```csharp
var report = await evaluator.GenerateReportAsync(userId, sessionId);

Console.WriteLine($"Overall Score: {report.OverallScore:F1}/100");
Console.WriteLine($"CCR: {report.Metrics.ContextCompressionRatio:P2}");
Console.WriteLine($"Recall@K: {report.Metrics.RecallAtKEfficiency:P1}");

foreach (var observation in report.Observations)
{
    Console.WriteLine($"- {observation}");
}
```

## Feature Status

### Completed (v0.7.0 - v0.11.0)
- [x] Core KPIs infrastructure
- [x] NIAH test framework (single-needle)
- [x] Cognitive scenario tests (False Memory, Cross-Session Retention)
- [x] OpenTelemetry metrics integration
- [x] Multi-needle evaluation (RULER-inspired, v0.11.0)
- [x] Query strategies: CombinedQuery, SeparateQueries, SequentialQueries
- [x] Recovery rate metrics and per-needle tracking

### Future Enhancements
- [ ] LongBench subset (QA, summarization)
- [ ] InfiniteBench (100K+ extreme tests)
- [ ] Automated scorecard generation

> See [Roadmap](ROADMAP.md) for full development timeline.

## References

- [NIAH Test](https://github.com/gkamradt/LLMTest_NeedleInAHaystack) - Original benchmark
- [RULER](https://arxiv.org/abs/2404.06654) - Multi-hop retrieval benchmark
- [LongBench](https://github.com/THUDM/LongBench) - Long context evaluation
- Atkinson-Shiffrin Multi-Store Model (1968)
- Baddeley's Working Memory Model (1974)
- Tulving's Memory Classification (1972)
