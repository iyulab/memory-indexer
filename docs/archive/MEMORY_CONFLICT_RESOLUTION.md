# Memory Conflict Resolution Design

## Philosophy: General-Purpose Memory Management

Memory-indexer is a **general-purpose memory system**, not a game-specific tool. The core operations must handle universal memory management patterns without domain-specific logic.

### Core Operations

1. **ADD**: Store new memory when no semantic conflict exists
2. **UPDATE**: Modify existing memory when new information refines it
3. **RESOLVE**: Handle contradictory memories based on recency, confidence, and plausibility

### Anti-Pattern: Domain-Specific Tags

❌ **Wrong** (Game-specific):
```csharp
if (memory.Content.Contains("[QUESTION_R"))  // Game logic in core!
    return MemoryAction.Skip;
```

✅ **Correct** (Semantic):
```csharp
var similarity = await ComputeSemanticSimilarityAsync(newMemory, existingMemory);
if (similarity >= threshold)
    return await ResolveConflictAsync(newMemory, existingMemory);
```

---

## Research-Based Design

### 1. Semantic Deduplication (Multi-Stage)

Based on research from *SemDeDup*, *AgentCore*, and embedding-based approaches:

```
Stage 1: Exact Hash Matching (O(1))
  → If identical content hash exists, skip (duplicate)

Stage 2: High Similarity Detection (embedding cosine)
  → If similarity > HIGH_THRESHOLD (e.g., 0.95):
      - "I love pizza" vs "I really love pizza"
      - Same semantic meaning, no conflict
      → NO-OP (preserve existing)

Stage 3: Semantic Conflict Detection (0.7 - 0.95)
  → If similarity in moderate range:
      - Potential update or refinement
      → Analyze for contradiction
      → If contradictory: RESOLVE
      → If refinement: UPDATE
      → If unrelated: ADD

Stage 4: Low Similarity (< 0.7)
  → Different topics/facts
  → ADD as new memory
```

### 2. Contradiction Resolution

Based on *Memoria* recency-weighting and *AgentCore* consolidation:

```csharp
public enum ConflictType
{
    None,              // No contradiction
    Refinement,        // New info adds detail ("like" → "love")
    Update,            // Same fact, updated value ("age 25" → "age 26")
    Contradiction,     // Direct conflict ("like apples" → "dislike apples")
    Temporal           // Time-based change ("used to like" vs "now dislike")
}

public async Task<MemoryAction> ResolveConflictAsync(
    MemoryUnit newMemory,
    MemoryUnit existingMemory)
{
    var conflictType = await DetectConflictTypeAsync(newMemory, existingMemory);

    return conflictType switch
    {
        ConflictType.None => MemoryAction.NoOp,
        ConflictType.Refinement => MemoryAction.Merge,
        ConflictType.Update => MemoryAction.Replace,
        ConflictType.Contradiction => await ResolveByRecencyAsync(newMemory, existingMemory),
        ConflictType.Temporal => MemoryAction.Archive,  // Keep old, add new with temporal marker
        _ => MemoryAction.Add
    };
}
```

### 3. Recency-Weighted Resolution

From *Memoria* exponential decay pattern:

```csharp
public class RecencyWeightedResolver
{
    private const float DECAY_RATE = 0.1f;  // Exponential decay parameter

    public float ComputeRecencyWeight(DateTime timestamp)
    {
        var ageInDays = (DateTime.UtcNow - timestamp).TotalDays;
        return (float)Math.Exp(-DECAY_RATE * ageInDays);
    }

    public async Task<MemoryAction> ResolveByRecencyAsync(
        MemoryUnit newMemory,
        MemoryUnit existingMemory)
    {
        var newWeight = ComputeRecencyWeight(newMemory.Timestamp);
        var existingWeight = ComputeRecencyWeight(existingMemory.Timestamp);

        // Combine recency with confidence
        var newScore = newWeight * newMemory.Confidence;
        var existingScore = existingWeight * existingMemory.Confidence;

        if (newScore > existingScore * 1.2f)  // Require 20% advantage
        {
            // New memory supersedes old
            return MemoryAction.Replace;
        }
        else if (existingScore > newScore * 1.2f)
        {
            // Keep existing, archive new as alternative
            return MemoryAction.NoOp;
        }
        else
        {
            // Too close to call, store both with conflict marker
            return MemoryAction.MarkConflict;
        }
    }
}
```

### 4. LLM-Based Conflict Detection

From *AgentCore* consolidation prompt pattern:

```csharp
public interface IMemoryConflictDetector
{
    /// <summary>
    /// Detects semantic conflicts between memories using LLM reasoning.
    /// </summary>
    Task<ConflictAnalysis> AnalyzeConflictAsync(
        MemoryUnit newMemory,
        MemoryUnit existingMemory,
        CancellationToken cancellationToken = default);
}

public class LlmConflictDetector : IMemoryConflictDetector
{
    private readonly ITextCompletionService _llm;

    public async Task<ConflictAnalysis> AnalyzeConflictAsync(
        MemoryUnit newMemory,
        MemoryUnit existingMemory,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildConflictAnalysisPrompt(newMemory, existingMemory);
        var response = await _llm.CompleteAsync(prompt, new TextCompletionOptions
        {
            Temperature = 0.1f,  // Low temperature for deterministic analysis
            MaxTokens = 300
        }, cancellationToken);

        return ParseConflictAnalysis(response);
    }

    private static string BuildConflictAnalysisPrompt(
        MemoryUnit newMemory,
        MemoryUnit existingMemory)
    {
        return $$$"""
            Analyze the relationship between these two memories:

            Memory A (existing): {{{existingMemory.Content}}}
            Timestamp: {{{existingMemory.Timestamp:O}}}
            Confidence: {{{existingMemory.Confidence}}}

            Memory B (new): {{{newMemory.Content}}}
            Timestamp: {{{newMemory.Timestamp:O}}}
            Confidence: {{{newMemory.Confidence}}}

            Determine the relationship type:
            1. IDENTICAL - Same semantic meaning (e.g., "likes pizza" vs "enjoys pizza")
            2. REFINEMENT - B adds detail to A (e.g., "likes pizza" → "loves margherita pizza")
            3. UPDATE - Same fact, changed value (e.g., "age 25" → "age 26")
            4. CONTRADICTION - Direct conflict (e.g., "likes apples" → "dislikes apples")
            5. TEMPORAL - Time-based evolution (e.g., "used to smoke" vs "quit smoking")
            6. UNRELATED - Different topics

            Respond ONLY with JSON:
            {
              "conflictType": "IDENTICAL|REFINEMENT|UPDATE|CONTRADICTION|TEMPORAL|UNRELATED",
              "confidence": 0.0-1.0,
              "reasoning": "brief explanation",
              "recommendedAction": "NO_OP|MERGE|REPLACE|ARCHIVE|ADD"
            }
            """;
    }
}
```

### 5. Plausibility-Based Conflict Scoring

From research on *Knowledge Conflict* (NC/HPC/LPC):

```csharp
public enum PlausibilityLevel
{
    NoContradiction,        // NC: Fully compatible
    HighPlausibility,       // HPC: Slightly different but both plausible
    LowPlausibility         // LPC: Strong contradiction
}

public class PlausibilityAnalyzer
{
    public async Task<PlausibilityLevel> AssessPlausibilityAsync(
        MemoryUnit memory1,
        MemoryUnit memory2)
    {
        // Extract key entities and relations
        var entities1 = await ExtractEntitiesAsync(memory1.Content);
        var entities2 = await ExtractEntitiesAsync(memory2.Content);

        // Check for direct contradictions
        var hasContradiction = DetectDirectContradiction(entities1, entities2);

        if (!hasContradiction)
            return PlausibilityLevel.NoContradiction;

        // Assess plausibility of contradiction
        var contradictionStrength = ComputeContradictionStrength(entities1, entities2);

        return contradictionStrength > 0.7f
            ? PlausibilityLevel.LowPlausibility
            : PlausibilityLevel.HighPlausibility;
    }

    private bool DetectDirectContradiction(
        EntitySet entities1,
        EntitySet entities2)
    {
        // Example: "User likes X" vs "User dislikes X"
        foreach (var e1 in entities1.Relations)
        {
            foreach (var e2 in entities2.Relations)
            {
                if (e1.Subject == e2.Subject &&
                    e1.Object == e2.Object &&
                    AreOpposingPredicates(e1.Predicate, e2.Predicate))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool AreOpposingPredicates(string pred1, string pred2)
    {
        var opposites = new[]
        {
            ("likes", "dislikes"),
            ("loves", "hates"),
            ("is", "is not"),
            ("can", "cannot"),
            ("has", "lacks")
        };

        foreach (var (p1, p2) in opposites)
        {
            if ((pred1.Contains(p1) && pred2.Contains(p2)) ||
                (pred1.Contains(p2) && pred2.Contains(p1)))
                return true;
        }
        return false;
    }
}
```

---

## Example: Apple Preference Contradiction

User's scenario: `"과거-난 사과를 좋아해, 현재-사과 먹고 탈나서 이제 사과를 안먹어"`

### Memory Evolution:

```csharp
// Stage 1: Initial memory
var memory1 = new MemoryUnit
{
    Content = "User likes apples",
    Timestamp = DateTime.Parse("2024-01-01"),
    Confidence = 0.8f,
    Type = MemoryType.Semantic
};

// Stage 2: Contradictory memory
var memory2 = new MemoryUnit
{
    Content = "User doesn't eat apples anymore after getting sick from them",
    Timestamp = DateTime.Parse("2024-06-15"),
    Confidence = 0.9f,
    Type = MemoryType.Semantic
};

// Conflict Detection
var analysis = await conflictDetector.AnalyzeConflictAsync(memory1, memory2);
// Result: ConflictType.TEMPORAL with high confidence

// Resolution
var action = await resolver.ResolveConflictAsync(memory1, memory2);
// Result: MemoryAction.Archive (preserve old as historical context)

// Final State:
// Memory 1: "User liked apples" [Archived, ValidUntil: 2024-06-15]
// Memory 2: "User doesn't eat apples anymore after getting sick" [Active]
```

---

## Implementation Interfaces

### Core Contracts

```csharp
namespace MemoryIndexer.Interfaces;

/// <summary>
/// Detects and resolves semantic conflicts between memories.
/// Phase 26: Memory Conflict Resolution.
/// </summary>
public interface IMemoryConflictResolver
{
    /// <summary>
    /// Analyzes relationship between new and existing memory.
    /// </summary>
    Task<ConflictResolution> ResolveAsync(
        MemoryUnit newMemory,
        IReadOnlyList<MemoryUnit> similarMemories,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of conflict resolution analysis.
/// </summary>
public sealed class ConflictResolution
{
    /// <summary>
    /// Recommended action for the new memory.
    /// </summary>
    public required MemoryAction Action { get; init; }

    /// <summary>
    /// Type of conflict detected.
    /// </summary>
    public required ConflictType ConflictType { get; init; }

    /// <summary>
    /// Confidence in the resolution (0-1).
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// If Action is UPDATE or MERGE, this is the target memory ID.
    /// </summary>
    public string? TargetMemoryId { get; init; }

    /// <summary>
    /// If Action is ARCHIVE, this is the updated content.
    /// </summary>
    public string? UpdatedContent { get; init; }

    /// <summary>
    /// Human-readable reasoning for the decision.
    /// </summary>
    public string? Reasoning { get; init; }
}

/// <summary>
/// Actions to take when storing a new memory.
/// </summary>
public enum MemoryAction
{
    /// <summary>
    /// Add as new memory (no conflict).
    /// </summary>
    Add,

    /// <summary>
    /// Skip storing (duplicate exists).
    /// </summary>
    NoOp,

    /// <summary>
    /// Replace existing memory with new one.
    /// </summary>
    Replace,

    /// <summary>
    /// Merge new info into existing memory.
    /// </summary>
    Merge,

    /// <summary>
    /// Archive old memory, add new as current.
    /// </summary>
    Archive,

    /// <summary>
    /// Store both with conflict marker for review.
    /// </summary>
    MarkConflict
}

/// <summary>
/// Types of semantic conflicts between memories.
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// No conflict detected.
    /// </summary>
    None,

    /// <summary>
    /// Identical semantic meaning.
    /// </summary>
    Duplicate,

    /// <summary>
    /// New memory adds detail to existing.
    /// </summary>
    Refinement,

    /// <summary>
    /// Same fact, updated value.
    /// </summary>
    Update,

    /// <summary>
    /// Direct contradiction.
    /// </summary>
    Contradiction,

    /// <summary>
    /// Time-based evolution (preferences change).
    /// </summary>
    Temporal
}
```

---

## Integration with VCM Architecture

### Where Conflict Resolution Fits

```
┌─────────────────────────────────────────────────────┐
│  Recently Buffer (Tier 0)                           │
│  - Raw conversation staging                         │
└───────────────────────┬─────────────────────────────┘
                        │ Promotion
┌───────────────────────▼─────────────────────────────┐
│  Working Memory (L1)                                │
│  - Topic-grouped chunks                             │
│  ┌─────────────────────────────────────────┐       │
│  │ CONFLICT RESOLUTION HERE                │       │
│  │ Before promoting to Session/User        │       │
│  └─────────────────────────────────────────┘       │
└───────────────────────┬─────────────────────────────┘
                        │ Promotion (conflict-resolved)
┌───────────────────────▼─────────────────────────────┐
│  Session Memory (L2)                                │
│  - Deduplicated, conflict-free facts                │
└───────────────────────┬─────────────────────────────┘
                        │ Promotion (high confidence)
┌───────────────────────▼─────────────────────────────┐
│  User Profile (L3)                                  │
│  - Long-term, conflict-resolved knowledge           │
└─────────────────────────────────────────────────────┘
```

### Promotion Flow with Conflict Resolution

```csharp
// WorkingMemoryOrchestrator.cs
public async Task PromoteToSessionAsync(WorkingMemoryItem item)
{
    // Extract facts from working memory
    var facts = await ExtractFactsAsync(item.Content);

    foreach (var fact in facts)
    {
        // Search for similar existing memories
        var similar = await _sessionStore.SearchAsync(fact.Content, topK: 5);

        if (similar.Any())
        {
            // Resolve conflicts before storing
            var resolution = await _conflictResolver.ResolveAsync(
                fact,
                similar.Select(s => s.Memory).ToList()
            );

            await ExecuteResolutionAsync(resolution, fact);
        }
        else
        {
            // No conflict, store directly
            await _sessionStore.StoreAsync(fact);
        }
    }
}

private async Task ExecuteResolutionAsync(
    ConflictResolution resolution,
    MemoryUnit newMemory)
{
    switch (resolution.Action)
    {
        case MemoryAction.Add:
            await _sessionStore.StoreAsync(newMemory);
            break;

        case MemoryAction.NoOp:
            // Skip, duplicate exists
            break;

        case MemoryAction.Replace:
            await _sessionStore.DeleteAsync(resolution.TargetMemoryId!);
            await _sessionStore.StoreAsync(newMemory);
            break;

        case MemoryAction.Merge:
            var existing = await _sessionStore.GetByIdAsync(resolution.TargetMemoryId!);
            var merged = MergeMemories(existing, newMemory);
            await _sessionStore.UpdateAsync(merged);
            break;

        case MemoryAction.Archive:
            var old = await _sessionStore.GetByIdAsync(resolution.TargetMemoryId!);
            old.Metadata["Archived"] = "true";
            old.Metadata["ValidUntil"] = DateTime.UtcNow.ToString("O");
            await _sessionStore.UpdateAsync(old);
            await _sessionStore.StoreAsync(newMemory);
            break;

        case MemoryAction.MarkConflict:
            newMemory.Metadata["ConflictWith"] = resolution.TargetMemoryId!;
            newMemory.Metadata["ConflictReason"] = resolution.Reasoning ?? "";
            await _sessionStore.StoreAsync(newMemory);
            break;
    }
}
```

---

## Benefits of This Design

### 1. **Domain-Agnostic**
- No game-specific logic (`[QUESTION_R]` tags)
- Works for any application: chatbots, assistants, knowledge bases

### 2. **Research-Based**
- AgentCore consolidation patterns
- Memoria recency weighting
- SemDeDup multi-stage deduplication
- Plausibility-based conflict detection

### 3. **LLM-Powered Intelligence**
- Semantic conflict detection via LLM reasoning
- Natural language understanding of contradictions
- Context-aware resolution strategies

### 4. **Temporal Awareness**
- Handles preference changes over time
- Archives historical context
- Recency-weighted conflict resolution

### 5. **Confidence-Driven**
- Combines recency with confidence scores
- Requires significant advantage for replacement
- Marks conflicts when too close to call

---

## Next Steps (Phase 26 Implementation)

1. **Create Interfaces** (`IMemoryConflictResolver`, `IConflictDetector`)
2. **Implement LLM-Based Detector** (`LlmConflictDetector`)
3. **Implement Recency Resolver** (`RecencyWeightedResolver`)
4. **Integrate with VCM** (WorkingMemoryOrchestrator promotion flow)
5. **Add Tests** (apple preference contradiction, refinement, updates)
6. **Update ROADMAP.md** (Add Phase 26: Memory Conflict Resolution)

---

## References

- **AgentCore Memory Consolidation**: AWS ML Blog (ADD/UPDATE/NO-OP pattern)
- **Memoria Recency Weighting**: arXiv 2512.12686v1 (exponential decay)
- **SemDeDup**: OpenReview (multi-stage semantic deduplication)
- **Knowledge Conflict**: Research on NC/HPC/LPC plausibility
- **MaRS Architecture**: Cognitive memory architecture patterns
