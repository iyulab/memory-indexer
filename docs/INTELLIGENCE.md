# Advanced Intelligence Features

Memory Indexer exposes SDK intelligence capabilities through MCP tools, enabling LLMs to leverage semantic understanding, conflict resolution, adaptive retrieval, and graph analysis.

> **Note**: Intelligence features were introduced in v0.5.0 and continue to evolve. See [Roadmap](ROADMAP.md) for latest feature status.

---

## Table of Contents

- [Conflict Resolution](#conflict-resolution)
- [Adaptive Retrieval](#adaptive-retrieval)
- [Graph Traversal](#graph-traversal)
- [Efficiency Features](#efficiency-features)
- [Configuration Validation](#configuration-validation)
- [OpenTelemetry Metrics](#opentelemetry-metrics)

---

## Conflict Resolution

Detect and resolve contradictions between new information and existing memories.

### MCP Tools

| Tool | Description |
|------|-------------|
| `DetectContradiction` | Check if new content contradicts existing memories |
| `ResolveContradiction` | Resolve contradiction using specified strategy |
| `AutoResolveContradiction` | Automatically detect and resolve contradictions |
| `GetResolutionStrategy` | Get recommendation for handling contradiction types |

### Contradiction Types

| Type | Description | Example |
|------|-------------|---------|
| `Factual` | Conflicting facts | "User is 25" vs "User is 30" |
| `Temporal` | Time-based conflicts | "Meeting at 2pm" vs "Meeting at 3pm" |
| `Preference` | Preference changes | "Likes coffee" vs "Prefers tea now" |
| `Semantic` | Different meanings | Contextual interpretation conflicts |
| `Logical` | Logical inconsistencies | Self-contradicting statements |

### Resolution Strategies

| Strategy | When to Use |
|----------|-------------|
| `RecencyFirst` | Newer information supersedes older (default) |
| `ConfidenceFirst` | Higher confidence wins |
| `SourceAuthority` | Based on source trustworthiness |
| `AskUser` | Requires user intervention |
| `KeepBoth` | Maintain both versions |
| `TemporalPartition` | Both valid in different time contexts |

### Usage Examples

**Detect before storing:**
```csharp
// Via MCP tool call
DetectContradiction(
    content: "User's email is alice@new.com",
    similarityThreshold: 0.7,
    minConfidence: 0.6,
    limit: 50
)
// Returns: HasContradiction, ContradictionType, SuggestedStrategy
```

**Auto-resolve workflow:**
```csharp
// Single call to detect and resolve
AutoResolveContradiction(
    content: "User prefers dark mode",
    applyResolution: true  // Actually apply changes
)
// Returns: HasContradiction, AppliedStrategy, ActionsApplied
```

**Strategy recommendation:**
```csharp
GetResolutionStrategy(
    contradictionType: "Temporal",
    confidence: 0.85
)
// Returns: RecommendedStrategy, Explanation, AvailableStrategies
```

### Programmatic Usage

```csharp
public class ConflictAwareService
{
    private readonly IContradictionDetector _detector;
    private readonly IContradictionResolver _resolver;
    private readonly IMemoryPrimitives _memory;
    private readonly IMemoryStore _store;

    public ConflictAwareService(
        IContradictionDetector detector, IContradictionResolver resolver, IMemoryPrimitives memory, IMemoryStore store) =>
        (_detector, _resolver, _memory, _store) = (detector, resolver, memory, store);

    public async Task<bool> SafeStore(string userId, string content)
    {
        // Get existing related memories
        var existing = await _memory.RetrieveAsync(new RetrieveRequest { UserId = userId, Query = content, Limit = 50 });

        var newMemory = new MemoryUnit
        {
            UserId = userId,
            Content = content,
            Type = MemoryType.Fact
        };

        // Detect contradictions
        var analysis = await _detector.DetectMemoryContradictionAsync(
            newMemory,
            existing.Select(r => r.Memory).ToList());

        if (!analysis.HasContradiction)
        {
            await _memory.EncodeAsync(new EncodeRequest { UserId = userId, Content = content, Type = MemoryType.Fact });
            return true;
        }

        // Auto-resolve
        var resolution = await _resolver.AutoResolveMemoryAsync(analysis);

        if (resolution.RequiresUserIntervention)
            return false; // Need user input

        // Apply resolution
        if (resolution.SupersededItem != null)
            await _store.DeleteAsync(userId, resolution.SupersededItem.Id);

        await _memory.EncodeAsync(new EncodeRequest { UserId = userId, Content = content, Type = MemoryType.Fact });
        return true;
    }
}
```

---

## Adaptive Retrieval

Intelligent retrieval that automatically selects the optimal strategy based on query intent.

### MCP Tools

| Tool | Description |
|------|-------------|
| `ClassifyQueryIntent` | Analyze query to determine intent type |
| `AdaptiveRecall` | Smart retrieval with auto-selected strategy |
| `TieredRecall` | Retrieve from specific tiers with custom priority |
| `GetRetrievalRecommendation` | Get strategy recommendations for information types |

### Query Intent Types

| Intent | Description | Tier Priority | Example |
|--------|-------------|---------------|---------|
| `Factual` | Verified facts and preferences | Archive → Long | "What's my email?" |
| `Contextual` | Recent conversation context | Short → Buffer | "Tell me more about that" |
| `Temporal` | Time-based queries | Long → Archive | "What did we discuss yesterday?" |
| `Relational` | Entity relationships | Archive → Long | "What's related to X?" |
| `General` | Balanced multi-tier | All tiers | General queries |

### Strategy Selection

```
Query Analysis → Intent Classification → Tier Priority → Retrieval Execution
      ↓                   ↓                    ↓                 ↓
  Keywords         Factual/Temporal       Archive first     Similarity search
  Entities         Contextual/Relational  Buffer first      Graph expansion
  Time refs        General                Balanced          Hybrid search
```

### Usage Examples

**Classify before retrieval:**
```csharp
ClassifyQueryIntent(
    query: "What was my favorite color we discussed last week?",
    context: "Previous conversation about preferences"
)
// Returns: Intent=Temporal, Confidence=0.85,
//          SuggestedTierPriority=[Long, Archive]
```

**Adaptive retrieval (recommended):**
```csharp
AdaptiveRecall(
    query: "user preferences",
    context: "Setting up recommendations",
    maxResults: 10
)
// Returns: DetectedIntent, AppliedStrategy, Memories with relevance scores
```

**Manual tier control:**
```csharp
TieredRecall(
    query: "recent conversation",
    tierPriority: "Short,Buffer,Long",  // Custom order
    maxResults: 5
)
// Returns: ResultsPerTier breakdown, Memories from specified tiers
```

### Programmatic Usage

```csharp
public class SmartRetrievalService
{
    private readonly ITieredRetrievalStrategy _retriever;   // registered by AddMemoryIndexer(); classifies the query itself
    private readonly ILogger<SmartRetrievalService> _logger;

    public SmartRetrievalService(ITieredRetrievalStrategy retriever, ILogger<SmartRetrievalService> logger) =>
        (_retriever, _logger) = (retriever, logger);

    public async Task<List<MemoryUnit>> GetRelevantContext(
        string userId,
        string query,
        string? conversationContext = null)
    {
        var request = new TieredRetrievalRequest
        {
            Query = query,
            UserId = userId,
            ConversationContext = conversationContext,
            MaxResults = 10,
            MinSimilarity = 0.5f,
            IncludeGraphContext = true
        };

        var result = await _retriever.RetrieveAsync(request);

        // Log the strategy used
        _logger.LogInformation(
            "Retrieved {Count} memories using {Intent} strategy",
            result.MergedResults.Count,
            result.Intent.Intent);

        return result.MergedResults
            .Select(r => r.Memory)
            .ToList();
    }
}
```

---

## Graph Traversal

Navigate and analyze the knowledge graph for relationships, communities, and importance.

### MCP Tools

#### Community Detection
| Tool | Description |
|------|-------------|
| `DetectCommunities` | Find memory clusters using Label Propagation |
| `GetCommunityMemories` | Get all memories in a community |
| `GetCommunitySummary` | Get topic labels and key entities |

#### Importance Propagation
| Tool | Description |
|------|-------------|
| `ComputeImportance` | Run PageRank on entity graph |
| `GetEntityImportance` | Get importance score for an entity |
| `GetTopEntities` | Get ranked important entities |

#### Graph Traversal
| Tool | Description |
|------|-------------|
| `FindRelatedMemories` | Find memories through shared entities |
| `ExtractSubgraph` | Extract focused subgraph around memories |

### Algorithms

**Label Propagation (Community Detection):**
- Assigns community labels based on neighbor majority
- Iterates until convergence or max iterations
- Returns: Community assignments, Modularity score

**PageRank (Importance):**
- Propagates importance through entity relationships
- Configurable damping factor (default: 0.85)
- Returns: Entity importance scores, Rankings

### Usage Examples

**Discover thematic clusters:**
```csharp
DetectCommunities(
    maxIterations: 20,
    minCommunitySize: 2
)
// Returns: CommunityCount, Communities with member counts

GetCommunitySummary(communityId: 1)
// Returns: TopicLabel, KeyEntities, CommonPredicates
```

**Find important entities:**
```csharp
ComputeImportance(
    dampingFactor: 0.85,
    maxIterations: 50
)
// Returns: EntityCount, TopEntities ranked by score

GetTopEntities(topK: 10)
// Returns: Ranked entities with scores and connection counts
```

**Explore relationships:**
```csharp
FindRelatedMemories(
    memoryId: "guid-here",
    maxHops: 2,
    topK: 10
)
// Returns: Related memories with SharedEntities, ConnectionPath

ExtractSubgraph(
    memoryIds: "guid1,guid2",
    maxHops: 2,
    maxMemories: 20
)
// Returns: MemoryNodes, Entities, Triples, FormattedContext
```

### Programmatic Usage

```csharp
using System.Text;

public class KnowledgeExplorer
{
    private readonly ICommunityDetector _community;
    private readonly IImportancePropagator _importance;

    public KnowledgeExplorer(ICommunityDetector community, IImportancePropagator importance) =>
        (_community, _importance) = (community, importance);

    public async Task<string> GetUserKnowledgeSummary(string userId)
    {
        // Detect communities
        var communities = await _community.DetectCommunitiesAsync(
            userId,
            new CommunityDetectionOptions { MinCommunitySize = 3 });

        // Get top entities
        var topEntities = await _importance.GetTopEntitiesAsync(userId, 5);

        // Build summary
        var sb = new StringBuilder();
        sb.AppendLine($"Knowledge organized into {communities.CommunityCount} topics:");

        foreach (var (id, size) in communities.CommunitySizes.Take(3))
        {
            var summary = await _community.GetCommunitySummaryAsync(id, userId);
            sb.AppendLine($"- {summary.TopicLabel}: {size} memories");
        }

        sb.AppendLine("\nKey entities:");
        foreach (var entity in topEntities)
        {
            sb.AppendLine($"- {entity.EntityName} (importance: {entity.Score:F2})");
        }

        return sb.ToString();
    }
}
```

---

## Efficiency Features

Production optimizations for high-performance memory operations.

### Session-Level Recall Caching

Eliminates redundant embedding generation and vector searches.

```csharp
// Configuration
services.AddMemoryIndexer(options =>
{
    options.Latency.QueryCacheTtlMinutes = 10;  // Cache TTL
    options.Latency.EmbeddingCacheEnabled = true;
    options.Latency.EmbeddingCacheSize = 1000;
});
```

**Features:**
- SHA256 cache keys for collision resistance
- Per-session caching with configurable TTL
- Cache statistics: hits, misses, duplicates, hit ratio

### Recall Pattern Analysis

Detects inefficient usage patterns for optimization.

```csharp
var patternAnalyzer = serviceProvider.GetRequiredService<IRecallPatternAnalyzer>();   // registered by AddMemoryIndexer()

// Get statistics
var stats = patternAnalyzer.GetStatistics(userId);
// Returns: DuplicateQueries, RapidFireCount, UniqueQueries

// Get active alerts
var alerts = patternAnalyzer.GetAlerts(userId);
// Returns: AlertType, Message, Severity

// Get recommendations
var recommendations = patternAnalyzer.GetRecommendations(userId);
// Returns: Caching suggestions, Batching opportunities, Query consolidation
```

**Detected Patterns:**
- Duplicate queries (exact matches within window)
- Rapid-fire recalls (many queries in short timespan)
- Inefficient query patterns

### Token Budget Monitoring

Track and manage token consumption per session.

```csharp
public class TokenAwareService
{
    private readonly ITokenBudgetMonitor _monitor;
    private readonly ILogger<TokenAwareService> _logger;

    public TokenAwareService(ITokenBudgetMonitor monitor, ILogger<TokenAwareService> logger) =>
        (_monitor, _logger) = (monitor, logger);

    public void StartConversation(string sessionId, string userId)
    {
        // Start monitoring with 8000 token budget
        _monitor.StartSession(sessionId, userId, maxTokenBudget: 8000, warningThreshold: 0.8f);

        // Subscribe to events
        _monitor.OnBudgetWarning += (s, e) =>
        {
            _logger.LogWarning("Token budget warning: {Ratio:P0}", e.UsageRatio);
        };

        _monitor.OnBudgetExceeded += (s, e) =>
        {
            _logger.LogError("Token budget exceeded for {SessionId}", e.SessionId);
        };
    }

    public string ProcessMessage(string sessionId, string content)
    {
        // Estimate tokens
        var estimated = _monitor.EstimateTokens(content);

        // Check if affordable
        if (!_monitor.CanAfford(sessionId, estimated))
        {
            var recommendation = _monitor.GetRecommendation(sessionId);
            return $"Budget constraint: {recommendation.Message}";
        }

        // Record usage
        _monitor.RecordTokenUsage(sessionId, estimated, "recall");

        // Get status
        var status = _monitor.GetSessionStatus(sessionId);
        _logger.LogDebug("Tokens: {Used}/{Max} ({Ratio:P0})",
            status.TotalTokens, status.MaxBudget, status.UsageRatio);

        return "processed";
    }
}
```

**Recommendation Types:**
| Type | Urgency | Action |
|------|---------|--------|
| `Continue` | 0.0 | Normal operation |
| `ReduceScope` | 0.3 | Limit recall results |
| `Compress` | 0.6 | Use summarization |
| `Conserve` | 0.8 | Essential operations only |
| `Stop` | 1.0 | Budget exceeded |

---

## Configuration Validation

The configuration is validated at startup with structured errors and warnings: `AddMemoryIndexer()` runs the
registered `IConfigurationValidator` when the options are first built. An error stops the host with an
`OptionsValidationException` naming each setting; a warning is logged.

### Usage

```csharp
services.AddMemoryIndexer(options =>
{
    options.Search.DefaultLimit = 0; // out of range: the host does not start
});

// The same result, read yourself (e.g. for a diagnostics endpoint)
var indexerOptions = new MemoryIndexerOptions();
var result = serviceProvider.GetRequiredService<IConfigurationValidator>().Validate(indexerOptions);

foreach (var warning in result.Warnings)
{
    logger.LogWarning("Config warning: {Path} - {Message}. Suggestion: {Suggestion}",
        warning.PropertyPath, warning.Message, warning.Suggestion);
}
```

### Validation Rules

**Errors (blocking):**
- Invalid threshold ranges (must be 0-1)
- Non-positive values for sizes/limits
- Required fields missing (e.g., ConnectionString for SQLite)
- Cross-field constraints (MaxLimit >= DefaultLimit)
- Type distribution sum != 1.0

**Warnings (non-blocking):**
- Working memory capacity outside Baddeley's 7±2 range
- Suboptimal configurations

### Example Validation Output

```json
{
  "isValid": false,
  "errors": [
    {
      "propertyPath": "Embedding.Dimensions",
      "message": "Embedding dimensions must be positive",
      "currentValue": 0,
      "expectedConstraint": "> 0"
    }
  ],
  "warnings": [
    {
      "propertyPath": "WorkingMemory.Capacity",
      "message": "Capacity outside Baddeley's 7±2 model range (5-11)",
      "suggestion": "Recommended: 7-9 for cognitive compliance"
    }
  ]
}
```

---

## OpenTelemetry Metrics

Comprehensive observability for intelligence operations.

### Available Metrics

**Counters:**
| Metric | Description |
|--------|-------------|
| `memory_indexer.intelligence.classifications` | Classification operations |
| `memory_indexer.intelligence.summarizations` | Summarization operations |
| `memory_indexer.intelligence.deduplications` | Deduplication operations |
| `memory_indexer.intelligence.conflict_detections` | Conflict detection operations |
| `memory_indexer.intelligence.entity_extractions` | Entity extraction operations |
| `memory_indexer.intelligence.rerankings` | Reranking operations |
| `memory_indexer.tier_promotions` | Tier promotion events |
| `memory_indexer.query_cache_hits` | Query cache hits |
| `memory_indexer.duplicate_recalls` | Duplicate recall queries |
| `memory_indexer.rapid_fire_recalls` | Rapid-fire pattern detections |
| `memory_indexer.token_budget_warnings` | Token budget warnings |
| `memory_indexer.token_budget_exceeded` | Token budget exceeded events |
| `memory_indexer.intelligence.graph_queries` | Graph traversal queries |

**Histograms:**
| Metric | Description |
|--------|-------------|
| `memory_indexer.intelligence.classification_latency` | Classification duration |
| `memory_indexer.intelligence.summarization_latency` | Summarization duration |
| `memory_indexer.intelligence.deduplication_latency` | Deduplication duration |
| `memory_indexer.intelligence.reranking_latency` | Reranking duration |
| `memory_indexer.intelligence.graph_query_latency` | Graph query duration |
| `memory_indexer.token_budget_usage` | Token usage ratio distribution |

### Configuration

```csharp
using OpenTelemetry.Metrics;

services.AddOpenTelemetry()
    .WithMetrics(builder =>
    {
        builder.AddMeter("MemoryIndexer");  // MemoryIndexerTelemetry.ServiceName — all metrics above
        builder.AddOtlpExporter();          // or the exporter your backend reads (Prometheus, Azure Monitor, ...)
    });
```

Traces use an `ActivitySource` of the same name (`.WithTracing(b => b.AddSource("MemoryIndexer"))`).
`services.AddMemoryIndexerObservability(...)` registers both, with console and OTLP exporters.

### Prometheus Example

```
# Query cache hits against recalls
memory_indexer_query_cache_hits_total 145
memory_indexer_recalls_total 168

# Intelligence operations
memory_indexer_intelligence_classifications_total 500
memory_indexer_intelligence_classification_latency_milliseconds_bucket{le="100"} 485

# Token budget
memory_indexer_token_budget_warnings_total 12
memory_indexer_token_budget_usage_bucket{le="0.8"} 95
```

### Dashboard Recommendations

1. **Cache Efficiency**: Query cache hit ratio, embedding cache utilization
2. **Intelligence Health**: Classification/deduplication success rates
3. **Resource Usage**: Token budget warnings, tier promotion rates
4. **Latency**: p50/p95/p99 for intelligence operations
5. **Graph Analysis**: Query patterns, community detection frequency

---

## Best Practices

### Conflict Resolution
- Always detect before storing potentially conflicting information
- Use `AutoResolveContradiction` for automated workflows
- Fall back to `AskUser` strategy for high-stakes conflicts

### Adaptive Retrieval
- Prefer `AdaptiveRecall` over manual tier selection
- Provide conversation context for better intent classification
- Use `TieredRecall` only when you need specific tier control

### Graph Traversal
- Run `DetectCommunities` periodically (not per-query)
- Cache `ComputeImportance` results with appropriate TTL
- Use `ExtractSubgraph` for focused context building

### Efficiency
- Enable query caching in production
- Set appropriate token budgets per session
- Monitor recall patterns and act on recommendations

### Configuration
- Validate configuration at startup
- Address warnings even if they're non-blocking
- Use environment-specific configurations

---

## Further Reading

- [Architecture](ARCHITECTURE.md) - System design and 3-axis model
- [Usage Guides](GUIDES.md) - Common patterns and best practices
- [Benchmarks](BENCHMARKS.md) - Performance measurements
