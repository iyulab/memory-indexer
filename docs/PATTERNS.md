# Common Patterns & Use Cases

Practical patterns for using Memory Indexer in real-world applications.

## Table of Contents

- [Conversation History](#conversation-history)
- [User Preferences](#user-preferences)
- [Long-term Facts](#long-term-facts)
- [Entity Relationships](#entity-relationships)
- [Session Continuity](#session-continuity)
- [Memory Reflection](#memory-reflection)
- [Advanced Retrieval](#advanced-retrieval)

---

## Conversation History

### Pattern: Automatic Conversation Archiving

Store conversation turns and let the 4-tier system automatically manage retention.

```csharp
public class ConversationService
{
    private readonly IVirtualContextManager _vcm;

    public async Task ProcessUserMessage(string userId, string message)
    {
        // Store in Recently Buffer (Tier 0)
        await _vcm.AddToRecentlyAsync(userId, message, metadata: new()
        {
            ["role"] = "user",
            ["timestamp"] = DateTime.UtcNow
        });

        // VCM automatically:
        // 1. Promotes to Working Memory after 60s OR 500 tokens OR 3 turns
        // 2. Promotes to Session Store after topic change OR 10min
        // 3. Summarizes and archives for long-term storage
    }

    public async Task<string> GetConversationContext(string userId)
    {
        // Retrieve hybrid context from all tiers
        var context = await _vcm.RetrieveHybridAsync(userId, limit: 10);

        return string.Join("\n\n", context.Select(m =>
            $"[{m.Metadata["role"]}]: {m.Content}"));
    }
}
```

**When to use**: Chat applications, conversational AI, dialogue systems

**Benefits**:
- Automatic summarization of old conversations
- Intelligent forgetting of irrelevant details
- Reduced token usage in LLM prompts

---

## User Preferences

### Pattern: Progressive Preference Learning

Learn user preferences through repeated confirmations, promoting to User Profile.

```csharp
public class PreferenceService
{
    private readonly IUserProfileService _profile;

    public async Task LearnPreference(string userId, string preference)
    {
        // Store as fact with category
        await _profile.StoreFactAsync(new UserFact
        {
            UserId = userId,
            Category = UserFactCategory.Preference,
            Content = preference,
            Confidence = 0.6, // Initial confidence
            Source = "conversation",
            FirstObserved = DateTime.UtcNow,
            LastObserved = DateTime.UtcNow,
            ObservationCount = 1
        });
    }

    public async Task ConfirmPreference(string userId, string preference)
    {
        var facts = await _profile.RecallFactsAsync(userId, preference);

        foreach (var fact in facts)
        {
            // Increment confirmation count and confidence
            await _profile.UpdateFactAsync(new UserFact
            {
                Id = fact.Id,
                UserId = fact.UserId,
                Category = fact.Category,
                Content = fact.Content,
                Confidence = Math.Min(1.0, fact.Confidence + 0.15),
                ObservationCount = fact.ObservationCount + 1,
                LastObserved = DateTime.UtcNow
            });
        }

        // Facts with Confidence >= 0.8 AND ObservationCount >= 3
        // are automatically promoted to User Profile (Tier 3)
    }

    public async Task<List<string>> GetConfirmedPreferences(string userId)
    {
        var facts = await _profile.RecallFactsAsync(userId,
            category: UserFactCategory.Preference);

        return facts
            .Where(f => f.Confidence >= 0.8 && f.ObservationCount >= 3)
            .Select(f => f.Content)
            .ToList();
    }
}
```

**When to use**: Personalization, recommendation systems, user profiling

**Benefits**:
- Progressive learning prevents false positives
- High-confidence facts persist long-term
- Automatic confidence scoring and promotion

---

## Long-term Facts

### Pattern: Structured Fact Storage

Store structured facts about entities with confidence tracking.

```csharp
public class FactLearningService
{
    private readonly IUserProfileService _profile;
    private readonly IMemoryPrimitives _memory;

    public async Task StoreFact(string userId, string entity, string attribute, string value)
    {
        // Store as structured fact
        await _profile.StoreFactAsync(new UserFact
        {
            UserId = userId,
            Category = UserFactCategory.Fact,
            Content = $"{entity}: {attribute} = {value}",
            Confidence = 0.7,
            Metadata = new Dictionary<string, object>
            {
                ["entity"] = entity,
                ["attribute"] = attribute,
                ["value"] = value
            }
        });
    }

    public async Task UpdateFactConfidence(string userId, string entity, bool confirmed)
    {
        var facts = await _profile.RecallFactsAsync(userId, entity);

        foreach (var fact in facts)
        {
            var adjustment = confirmed ? 0.2 : -0.3;
            var newConfidence = Math.Clamp(fact.Confidence + adjustment, 0.0, 1.0);

            await _profile.UpdateFactAsync(new UserFact
            {
                Id = fact.Id,
                UserId = fact.UserId,
                Category = fact.Category,
                Content = fact.Content,
                Confidence = newConfidence,
                ObservationCount = fact.ObservationCount + 1
            });
        }
    }

    public async Task<Dictionary<string, string>> GetEntityAttributes(
        string userId, string entity)
    {
        var facts = await _profile.RecallFactsAsync(userId, entity);

        return facts
            .Where(f => f.Metadata.ContainsKey("attribute") &&
                       f.Confidence >= 0.7)
            .ToDictionary(
                f => f.Metadata["attribute"].ToString()!,
                f => f.Metadata["value"].ToString()!
            );
    }
}
```

**When to use**: Knowledge bases, CRM systems, data enrichment

**Benefits**:
- Confidence-based fact retrieval
- Automatic contradiction resolution
- Structured entity-attribute-value storage

---

## Entity Relationships

### Pattern: Graph-based Relationship Tracking

Track relationships between entities using the graph memory network.

```csharp
public class RelationshipService
{
    private readonly IKnowledgeGraphService _graph;

    public async Task AddRelationship(
        string userId,
        string subject,
        string predicate,
        string obj)
    {
        await _graph.AddTripleAsync(new EntityTriple
        {
            UserId = userId,
            Subject = subject,
            Predicate = predicate,
            Object = obj,
            Confidence = 0.8,
            Metadata = new()
            {
                ["source"] = "user_statement",
                ["timestamp"] = DateTime.UtcNow
            }
        });
    }

    public async Task<List<string>> GetRelatedEntities(
        string userId,
        string entity,
        int maxDepth = 2)
    {
        // Find all entities connected to this entity within maxDepth hops
        var related = await _graph.GetRelatedEntitiesAsync(
            userId,
            entity,
            maxDepth);

        return related.Select(e => e.Name).ToList();
    }

    public async Task<List<EntityTriple>> GetEntityRelationships(
        string userId,
        string entity)
    {
        // Get all triples where entity is subject or object
        var asSubject = await _graph.GetTriplesAsync(userId, subject: entity);
        var asObject = await _graph.GetTriplesAsync(userId, obj: entity);

        return asSubject.Concat(asObject).ToList();
    }

    public async Task<Dictionary<string, double>> GetCommunityImportance(
        string userId)
    {
        // Use PageRank to identify important entities
        var communities = await _graph.DetectCommunitiesAsync(userId);
        var importance = await _graph.CalculatePageRankAsync(userId);

        return importance
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
```

**When to use**: Social networks, knowledge graphs, recommendation engines

**Benefits**:
- Automatic community detection
- PageRank-based importance scoring
- Multi-hop relationship queries

---

## Session Continuity

### Pattern: Cross-Session Context Restoration

Restore context from previous sessions automatically.

```csharp
public class SessionService
{
    private readonly ISessionStore _sessionStore;
    private readonly IWorkingMemory _workingMemory;

    public async Task StartSession(string userId, string sessionId)
    {
        // Load recent session summaries
        var recentSessions = await _sessionStore.GetRecentSessionsAsync(
            userId,
            limit: 3);

        // Promote relevant session context to Working Memory
        foreach (var session in recentSessions)
        {
            if (session.Summary != null)
            {
                await _workingMemory.PromoteAsync(new MemoryUnit
                {
                    UserId = userId,
                    SessionId = sessionId,
                    Content = session.Summary,
                    Type = MemoryType.Episodic,
                    ImportanceScore = 0.7
                });
            }
        }
    }

    public async Task<string> GenerateSessionSummary(string userId, string sessionId)
    {
        var session = await _sessionStore.GetSessionAsync(userId, sessionId);

        // Summarize session using LLM
        var summary = await SummarizeSessionAsync(session);

        // Store summary
        await _sessionStore.UpdateSessionAsync(userId, sessionId, summary);

        return summary;
    }

    public async Task<List<MemoryUnit>> GetSessionHistory(
        string userId,
        string sessionId)
    {
        return await _sessionStore.GetSessionMemoriesAsync(userId, sessionId);
    }
}
```

**When to use**: Multi-session applications, long-running projects, collaborative work

**Benefits**:
- Seamless context restoration
- Automatic session summarization
- Cross-session memory continuity

---

## Memory Reflection

### Pattern: Self-Directed Memory Analysis

Enable autonomous memory reflection and insight generation.

```csharp
public class ReflectionService
{
    private readonly ISelfDirectedMemoryManager _manager;

    public async Task ScheduleReflection(string userId)
    {
        // Start heartbeat-based reflection
        await _manager.StartHeartbeatAsync(userId, options: new()
        {
            HeartbeatInterval = TimeSpan.FromHours(1),
            EnableReflection = true,
            EnableContradictionDetection = true
        });
    }

    public async Task<List<string>> GetInsights(string userId)
    {
        // Retrieve generated insights from reflection
        var insights = await _manager.GetReflectionsAsync(userId);

        return insights
            .Where(i => i.Type == ReflectionType.Insight)
            .Select(i => i.Content)
            .ToList();
    }

    public async Task ResolveContradictions(string userId)
    {
        // Detect and resolve contradictions
        var contradictions = await _manager.DetectContradictionsAsync(userId);

        foreach (var contradiction in contradictions)
        {
            // Present to user or auto-resolve based on confidence
            if (contradiction.ConfidenceDelta > 0.3)
            {
                // Keep higher confidence fact, soft-delete lower
                await _manager.ResolveContradictionAsync(contradiction);
            }
        }
    }

    public async Task<Dictionary<string, object>> GetMemoryStats(string userId)
    {
        var stats = await _manager.GetMemoryStatisticsAsync(userId);

        return new Dictionary<string, object>
        {
            ["total_memories"] = stats.TotalMemories,
            ["active_working_memory"] = stats.WorkingMemoryCount,
            ["user_facts"] = stats.UserFactCount,
            ["confidence_distribution"] = stats.ConfidenceDistribution,
            ["memory_pressure"] = stats.MemoryPressureLevel
        };
    }
}
```

**When to use**: Autonomous agents, long-term learning systems, memory optimization

**Benefits**:
- Automatic insight generation
- Contradiction detection and resolution
- Self-correcting memory system

---

## Advanced Retrieval

### Pattern: Query Intent-Aware Retrieval

Optimize retrieval based on query intent classification.

```csharp
public class SmartRetrievalService
{
    private readonly ISmartRetrieval _retrieval;

    public async Task<List<MemoryUnit>> RetrieveByIntent(
        string userId,
        string query)
    {
        // Classify query intent: Factual, Contextual, Temporal, Relational
        var intent = await _retrieval.ClassifyQueryIntentAsync(query);

        // Retrieve with adaptive fidelity
        var results = await _retrieval.RetrieveWithIntentAsync(
            userId,
            query,
            intent,
            tokenBudget: 1000);

        return results.Memories.ToList();
    }

    public async Task<List<MemoryUnit>> RetrieveWithBudget(
        string userId,
        string query,
        int tokenBudget)
    {
        // Smart token allocation across tiers
        var allocation = await _retrieval.AllocateTokenBudgetAsync(
            tokenBudget,
            strategy: TokenAllocationStrategy.Recency);

        var results = await _retrieval.RetrieveMultiTierAsync(
            userId,
            query,
            allocation);

        return results.Memories.ToList();
    }

    public async Task<List<MemoryUnit>> RetrieveWithGraphExpansion(
        string userId,
        string query)
    {
        // Use graph relationships to expand query
        var expanded = await _retrieval.ExpandQueryWithGraphAsync(
            userId,
            query,
            maxHops: 2);

        var results = await _retrieval.RetrieveAsync(
            userId,
            expanded.ExpandedQuery,
            limit: 10);

        return results.Memories.ToList();
    }
}
```

**When to use**: Search applications, question answering, context-aware retrieval

**Benefits**:
- Intent-aware retrieval optimization
- Token budget management
- Graph-enhanced query expansion

---

## Anti-Patterns

### ❌ Storing Raw LLM Outputs Without Processing

**Don't**:
```csharp
// Storing raw LLM response without classification
await _memory.EncodeAsync(new MemoryUnit
{
    Content = llmResponse, // Raw, unprocessed
    Type = MemoryType.Episodic // Generic type
});
```

**Do**:
```csharp
// Classify and structure before storing
var classified = await _classifier.ClassifyAsync(llmResponse);
var entities = await _extractor.ExtractEntitiesAsync(llmResponse);

await _memory.EncodeAsync(new MemoryUnit
{
    Content = classified.Summary,
    Type = classified.Type,
    Entities = entities,
    ImportanceScore = classified.Importance
});
```

### ❌ Ignoring Memory Pressure

**Don't**:
```csharp
// Keep adding memories without checking pressure
while (true)
{
    await _memory.EncodeAsync(newMemory);
}
```

**Do**:
```csharp
// Monitor pressure and adapt
var pressure = _pressureMonitor.CurrentPressure;

if (pressure == MemoryPressureLevel.High)
{
    // Trigger proactive eviction
    await _workingMemory.EvictLeastRelevantAsync(count: 3);
}

await _memory.EncodeAsync(newMemory);
```

### ❌ Bypassing 4-Tier Architecture

**Don't**:
```csharp
// Direct storage bypass
await _memoryStore.StoreAsync(memory); // Skip VCM
```

**Do**:
```csharp
// Use VCM for automatic tier management
await _vcm.AddToRecentlyAsync(userId, content);
// Let VCM handle promotion pipeline
```

---

## Performance Tips

### Batch Operations

```csharp
// Good: Batch encode
var memories = messages.Select(m => new MemoryUnit { Content = m });
await _memory.EncodeBatchAsync(memories);

// Bad: Individual encodes
foreach (var m in messages)
{
    await _memory.EncodeAsync(new MemoryUnit { Content = m });
}
```

### Lazy Embedding Loading

```csharp
// Enable for memory-constrained environments
options.VCM.WorkingMemory.LazyEmbeddingLoading = true;

// Reduces Working Memory footprint by ~3KB per item
```

### Token Budget Optimization

```csharp
// Allocate tokens based on query intent
var budget = queryType switch
{
    QueryIntent.Factual => 500,      // Precise facts only
    QueryIntent.Contextual => 1500,  // Rich context needed
    QueryIntent.Temporal => 800,     // Time-ordered events
    _ => 1000
};

await _retrieval.RetrieveWithIntentAsync(userId, query, intent, budget);
```

---

## Next Steps

- **Production Deployment**: [Kubernetes Guide](../deploy/kubernetes/README.md)
- **Architecture Deep Dive**: [Architecture](ARCHITECTURE.md)
- **Integration Examples**: [Integrations](INTEGRATIONS.md)
- **Best Practices**: [Best Practices](BEST_PRACTICES.md)
