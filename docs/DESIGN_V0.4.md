# Memory Indexer v0.4.x Design Document

**Version**: 0.4.0
**Date**: 2026-01-07
**Status**: Approved for Implementation
**Base**: spec-suggest-02.md (3-Axis Model)

---

## Executive Summary

Memory Indexer v0.4.x introduces a **3-Axis Cognitive Memory Model** (Type × Scope × Tier) to support diverse LLM scenarios from ChatGPT-style conversations to games and RAG systems. This design maintains cognitive science foundations while adding **Progressive API** (Level 0-3) for zero-config simplicity to expert control.

### Key Changes from v0.3.x

| Aspect | v0.3.x | v0.4.x |
|--------|--------|--------|
| **Model** | 2-Axis (Type × Tier) | **3-Axis (Type × Scope × Tier)** |
| **Tier Names** | Sensory/Working/Episodic/Semantic | **Buffer/Short/Long/Archive** |
| **API** | Single level (Full Control) | **4 Levels (Zero-Config → Expert)** |
| **Scope** | Implicit (SessionId field) | **Explicit enum (User/Session/Topic/Turn)** |
| **Topic** | Manual | **Auto-detection (v0.5.x)** |
| **Domain** | Generic only | **DomainProfile system (v0.5.x)** |

### Design Principles

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   "Zero-Config Intelligence, Expert Control When Needed"       │
│                                                                 │
│   • Simple: userId + sessionId + content = Full functionality  │
│   • 3-Axis: Type × Scope × Tier (independent dimensions)       │
│   • Auto: Topic detection, Type classification, Promotion      │
│   • Flexible: DomainProfile for domain-specific optimization   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 1. 3-Axis Memory Model

### 1.1 Overview

Memory is represented as a 3D coordinate:

```
Memory = (Type, Scope, Tier)

Examples:
├── (Semantic, User, Archive)      = "Python developer" — permanent profile
├── (Episodic, Session, Long)      = "Fixed 3 bugs today" — session history
├── (Procedural, User, Long)       = "Code-first preference" — learned pattern
├── (Semantic, Topic, Short)       = "useEffect issue" — current topic fact
└── (Episodic, Turn, Buffer)       = "Just received code" — immediate processing
```

### 1.2 Axis Definitions

| Axis | Definition | Cognitive Basis |
|------|------------|-----------------|
| **Type** | Cognitive classification of content | Tulving (1972, 1985) - Episodic/Semantic/Procedural memory systems |
| **Scope** | Access range of information | Cowan (2001), Oberauer (2002) - Working memory scope |
| **Tier** | Lifespan and persistence | Atkinson-Shiffrin (1968) - Multi-store model |

---

## 2. TYPE Axis (Content Classification)

**Unchanged from v0.3.x** - Already aligned with Tulving's taxonomy:

```csharp
public enum MemoryType
{
    /// <summary>
    /// Episodic: Events and experiences with temporal context
    /// Example: "User asked about React hooks yesterday"
    /// </summary>
    Episodic = 0,

    /// <summary>
    /// Semantic: Facts and knowledge, context-independent
    /// Example: "User is a senior Python developer"
    /// </summary>
    Semantic = 1,

    /// <summary>
    /// Procedural: Rules, patterns, implicit knowledge
    /// Example: "When code review → check lint first"
    /// </summary>
    Procedural = 2
}
```

---

## 3. SCOPE Axis (Access Range)

### 3.1 NEW: Scope Enum

```csharp
/// <summary>
/// Scope of memory access - hierarchical access ranges
/// </summary>
public enum Scope
{
    /// <summary>
    /// Turn scope (S3) - Current turn buffer, internal only
    /// Lifespan: Until response generation complete
    /// Access: Internal processing only
    /// </summary>
    Turn = 0,

    /// <summary>
    /// Topic scope (S2) - Current topic discussion, internal only
    /// Lifespan: Until topic change detected (similarity < 0.4)
    /// Access: Internal processing only
    /// </summary>
    Topic = 1,

    /// <summary>
    /// Session scope (S1) - Conversation session, API exposed
    /// Lifespan: Until EndSessionAsync() called
    /// Access: Within sessionId boundary
    /// Isolation: sessionId
    /// </summary>
    Session = 2,

    /// <summary>
    /// User scope (S0) - Cross-session global, API exposed
    /// Lifespan: Permanent (until explicit delete)
    /// Access: All sessions for userId
    /// Isolation: userId
    /// </summary>
    User = 3
}
```

### 3.2 Scope Hierarchy

```
┌──────────────────────────────────────────────────────────────┐
│  USER (S0) — API Exposed                                     │
│  ────────────────────────────────────────────────────────    │
│  • Long-term profile, preferences                            │
│  • Cross-session knowledge                                   │
│  • Permanent (explicit delete only)                          │
│                              │                               │
│                              ▼                               │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  SESSION (S1) — API Exposed                            │ │
│  │  ────────────────────────────────────────────────────  │ │
│  │  • Conversation context                                │ │
│  │  • Session goals and files                             │ │
│  │  • Until EndSessionAsync()                             │ │
│  │                        │                               │ │
│  │                        ▼                               │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │  TOPIC (S2) — Internal Auto-Managed              │ │ │
│  │  │  ──────────────────────────────────────────────  │ │ │
│  │  │  • Current discussion topic                      │ │ │
│  │  │  • Auto-detected by semantic similarity          │ │ │
│  │  │  • Until topic change                            │ │ │
│  │  │              │                                    │ │ │
│  │  │              ▼                                    │ │ │
│  │  │  ┌────────────────────────────────────────────┐ │ │ │
│  │  │  │  TURN (S3) — Internal Auto-Managed         │ │ │
│  │  │  │  ────────────────────────────────────────  │ │ │
│  │  │  │  • Current turn input/output               │ │ │
│  │  │  │  • Recalled context for response           │ │ │
│  │  │  │  • Until response complete                 │ │ │
│  │  │  └────────────────────────────────────────────┘ │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 3.3 Scope Promotion Rules

```csharp
// Turn → Topic (automatic on response complete)
if (importance > 0.3f)
{
    memory.Scope = Scope.Topic;
    memory.TopicId = currentTopicId;
}

// Topic → Session (automatic on topic change)
if (topicChanged && importance > 0.5f)
{
    memory.Scope = Scope.Session;
    memory.TopicId = null;
}

// Session → User (automatic on EndSessionAsync)
if (confidence >= 0.8f && confirmCount >= 2 && canDecontextualize)
{
    memory.Scope = Scope.User;
    memory.SessionId = null;
}
```

---

## 4. TIER Axis (Lifespan)

### 4.1 Tier Redesign

**BREAKING CHANGE**: Rename tiers to align with lifespan semantics:

```csharp
/// <summary>
/// Memory tier - persistence and lifespan
/// </summary>
public enum Tier
{
    /// <summary>
    /// Buffer (T0) - Immediate processing
    /// Lifespan: Seconds to minutes
    /// Capacity: Unlimited (time-based expiry)
    /// Expiry: 60s idle OR 500 tokens OR 3 turns (OR logic)
    /// Cognitive: Sensory register (Atkinson-Shiffrin)
    /// </summary>
    Buffer = 0,

    /// <summary>
    /// Short (T1) - Active processing
    /// Lifespan: Minutes to hours
    /// Capacity: 4-7 chunks (Miller's Law)
    /// Expiry: 10min OR 2K tokens OR topic change (OR logic)
    /// Cognitive: Working memory (Baddeley)
    /// </summary>
    Short = 1,

    /// <summary>
    /// Long (T2) - Session persistence
    /// Lifespan: Hours to days
    /// Capacity: Configurable (default 10,000/user)
    /// Expiry: Ebbinghaus decay + importance weighting
    /// Cognitive: Long-term memory (active)
    /// </summary>
    Long = 2,

    /// <summary>
    /// Archive (T3) - Permanent storage
    /// Lifespan: Permanent
    /// Capacity: Configurable (default 1,000/user)
    /// Expiry: Explicit delete only
    /// Promotion: confidence >= 0.8 AND confirmCount >= 3
    /// Cognitive: Long-term memory (consolidated)
    /// </summary>
    Archive = 3
}
```

### 4.2 Mapping from v0.3.x

| v0.3.x | v0.4.x | Rationale |
|--------|--------|-----------|
| Sensory (T0) | **Buffer** (T0) | "Buffer" clearer than "Sensory" for developers |
| Working (T1) | **Short** (T1) | "Short-term" more intuitive than "Working" |
| Episodic (T2) | **Long** (T2) | "Long-term" avoids confusion with Type.Episodic |
| Semantic (T3) | **Archive** (T3) | "Archive" clearer than "Semantic" for permanent storage |

**Note**: v0.3.x tier names conflicted with Type names (Episodic, Semantic). v0.4.x removes this ambiguity.

---

## 5. API Design

### 5.1 Progressive API Levels

```
┌─────────────────────────────────────────────────────────────┐
│  Level 0: Zero-Config                                       │
│  ─────────────────────────────────────────────────────────  │
│  await memory.RememberAsync(userId, content);               │
│  var context = await memory.RecallAsync(userId, query);     │
│                                                              │
│  → sessionId auto-generated, everything automatic           │
│                                                              │
├─────────────────────────────────────────────────────────────┤
│  Level 1: Session-Aware (RECOMMENDED)                       │
│  ─────────────────────────────────────────────────────────  │
│  await memory.RememberAsync(userId, sessionId, content);    │
│  var context = await memory.RecallAsync(userId, sessionId,  │
│                                          query);             │
│                                                              │
│  → Topic/Turn/Tier/Type all automatic                       │
│                                                              │
├─────────────────────────────────────────────────────────────┤
│  Level 2: Type-Aware (GAMES, SPECIAL DOMAINS)               │
│  ─────────────────────────────────────────────────────────  │
│  await memory.StoreFactAsync(userId, sessionId, fact);      │
│  await memory.StoreEventAsync(userId, sessionId, event);    │
│  await memory.StoreRuleAsync(userId, sessionId, rule);      │
│                                                              │
│  → Type explicit, Scope/Tier automatic                      │
│                                                              │
├─────────────────────────────────────────────────────────────┤
│  Level 3: Full Control (EXPERT USE)                         │
│  ─────────────────────────────────────────────────────────  │
│  await memory.StoreAsync(new StoreRequest {                 │
│      UserId, SessionId, Content,                            │
│      Type, Scope, Tier, Importance, ...                     │
│  });                                                         │
│                                                              │
│  → All axes manually controlled                             │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 IMemoryService (Level 0-1)

```csharp
/// <summary>
/// Simple memory API - sufficient for 99% use cases
/// </summary>
public interface IMemoryService
{
    // ═══════════════════════════════════════════════════════════
    // CORE API — Build ChatGPT with just these 3 methods
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Store information - Type/Scope/Tier all automatic
    /// </summary>
    Task RememberAsync(string userId, string sessionId, string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recall relevant memories - smart cross-scope search
    /// </summary>
    /// <returns>Scope-structured context (User/Session/Topic)</returns>
    Task<MemoryContext> RecallAsync(string userId, string sessionId, string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// End session - auto cleanup and User scope promotion
    /// </summary>
    Task EndSessionAsync(string userId, string sessionId,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════
    // CONVENIENCE
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Delete all user memories (GDPR compliance)
    /// </summary>
    Task ForgetUserAsync(string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete specific session memories only
    /// </summary>
    Task ForgetSessionAsync(string userId, string sessionId,
        CancellationToken cancellationToken = default);
}
```

### 5.3 MemoryContext Return Structure

```csharp
/// <summary>
/// Recall result - scope-structured memories for LLM context
/// </summary>
public sealed class MemoryContext
{
    /// <summary>
    /// User scope memories (profile, long-term preferences)
    /// </summary>
    public required IReadOnlyList<Memory> UserMemories { get; init; }

    /// <summary>
    /// Session scope memories (current conversation context)
    /// </summary>
    public required IReadOnlyList<Memory> SessionMemories { get; init; }

    /// <summary>
    /// Topic scope memories (current topic-related, auto-detected)
    /// </summary>
    public required IReadOnlyList<Memory> TopicMemories { get; init; }

    /// <summary>
    /// Detected current topic (null if not detected)
    /// </summary>
    public string? DetectedTopic { get; init; }

    /// <summary>
    /// Total memory count across all scopes
    /// </summary>
    public int TotalCount => UserMemories.Count + SessionMemories.Count + TopicMemories.Count;

    /// <summary>
    /// Format for LLM prompt injection
    /// </summary>
    public string ToPrompt()
    {
        var sb = new StringBuilder();

        if (UserMemories.Any())
        {
            sb.AppendLine("=== About this user ===");
            foreach (var m in UserMemories.Take(5))
                sb.AppendLine($"- [{m.Type}] {m.Content}");
        }

        if (SessionMemories.Any())
        {
            sb.AppendLine("\n=== This conversation ===");
            foreach (var m in SessionMemories.Take(10))
                sb.AppendLine($"- {m.Content}");
        }

        if (TopicMemories.Any())
        {
            sb.AppendLine($"\n=== Current topic: {DetectedTopic} ===");
            foreach (var m in TopicMemories.Take(5))
                sb.AppendLine($"- {m.Content}");
        }

        return sb.ToString();
    }
}
```

### 5.4 IMemoryServiceAdvanced (Level 2-3)

```csharp
/// <summary>
/// Expert API - games, special domains, full control
/// </summary>
public interface IMemoryServiceAdvanced : IMemoryService
{
    // ═══════════════════════════════════════════════════════════
    // TYPE-SPECIFIC STORAGE (Level 2)
    // ═══════════════════════════════════════════════════════════

    Task StoreFactAsync(string userId, string sessionId, string fact,
        float? confidence = null, CancellationToken cancellationToken = default);

    Task StoreEventAsync(string userId, string sessionId, string eventDescription,
        CancellationToken cancellationToken = default);

    Task StoreRuleAsync(string userId, string sessionId, string rule,
        float? confidence = null, CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════
    // TYPE-SPECIFIC RECALL (Level 2)
    // ═══════════════════════════════════════════════════════════

    Task<IReadOnlyList<Memory>> RecallFactsAsync(string userId, string sessionId,
        string query, int limit = 10, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Memory>> RecallEventsAsync(string userId, string sessionId,
        string query, int limit = 10, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Memory>> GetApplicableRulesAsync(string userId, string sessionId,
        string context, CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════
    // FULL CONTROL (Level 3)
    // ═══════════════════════════════════════════════════════════

    Task<Memory> StoreAsync(StoreRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(SearchRequest request,
        CancellationToken cancellationToken = default);

    Task PromoteAsync(Guid memoryId, Scope targetScope,
        CancellationToken cancellationToken = default);

    Task ConvertTypeAsync(Guid memoryId, MemoryType targetType,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════
    // DOMAIN CONFIGURATION (v0.5.x)
    // ═══════════════════════════════════════════════════════════

    Task ApplyProfileAsync(string sessionId, DomainProfile profile,
        CancellationToken cancellationToken = default);
}
```

---

## 6. Data Schema Changes

### 6.1 MemoryUnit Extensions

```csharp
public sealed class MemoryUnit
{
    // ═══════════════════════════════════════════════════════════
    // EXISTING (unchanged)
    // ═══════════════════════════════════════════════════════════

    public Guid Id { get; set; }
    public string UserId { get; set; }
    public string? SessionId { get; set; }
    public string Content { get; set; }
    public ReadOnlyMemory<float>? Embedding { get; set; }
    public MemoryType Type { get; set; }
    public float Importance { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastAccessedAt { get; set; }

    // ═══════════════════════════════════════════════════════════
    // NEW for v0.4.x
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// NEW: Explicit scope (replaces implicit UserId/SessionId logic)
    /// </summary>
    public Scope Scope { get; set; } = Scope.Session;

    /// <summary>
    /// NEW: Topic identifier (internal, Scope.Topic only)
    /// </summary>
    public string? TopicId { get; set; }

    /// <summary>
    /// RENAMED: MemoryTier → Tier (Breaking change)
    /// Values: Buffer/Short/Long/Archive (new names)
    /// </summary>
    public Tier Tier { get; set; } = Tier.Short;

    /// <summary>
    /// NEW: Confidence score (Semantic/Procedural types)
    /// </summary>
    public float Confidence { get; set; } = 0.5f;

    /// <summary>
    /// NEW: Confirmation count (for User scope promotion)
    /// </summary>
    public int ConfirmCount { get; set; } = 0;

    /// <summary>
    /// NEW: Activation count (Procedural types)
    /// </summary>
    public int ActivationCount { get; set; } = 0;
}
```

### 6.2 Migration Impact

| Field | Change | Migration |
|-------|--------|-----------|
| `Scope` | NEW | Default `Scope.Session` for existing records |
| `TopicId` | NEW | Null for existing records |
| `Tier` | RENAMED from `MemoryTier` | Map: Sensory→Buffer, Working→Short, Episodic→Long, Semantic→Archive |
| `Confidence` | NEW | Default 0.5f |
| `ConfirmCount` | NEW | Default 0 |
| `ActivationCount` | NEW | Default 0 |

---

## 7. Automation Design (v0.5.x)

### 7.1 Topic Auto-Detection

```csharp
internal class TopicDetector
{
    private const float TOPIC_CHANGE_THRESHOLD = 0.4f;

    private string? _currentTopicId;
    private float[]? _currentTopicEmbedding;
    private readonly Queue<float[]> _recentEmbeddings = new(capacity: 5);

    public async Task<TopicChangeResult> DetectAsync(
        string content,
        IEmbeddingService embedding)
    {
        var contentEmbedding = await embedding.EmbedAsync(content);

        if (_currentTopicEmbedding == null)
        {
            return StartNewTopic(content, contentEmbedding);
        }

        var similarity = CosineSimilarity(_currentTopicEmbedding, contentEmbedding);

        if (similarity < TOPIC_CHANGE_THRESHOLD)
        {
            // Topic change detected
            var result = StartNewTopic(content, contentEmbedding);
            result.PreviousTopicId = _currentTopicId;
            result.IsTopicChange = true;
            return result;
        }

        // Same topic - update embedding
        UpdateTopicEmbedding(contentEmbedding);

        return new TopicChangeResult
        {
            TopicId = _currentTopicId!,
            IsTopicChange = false
        };
    }

    private void UpdateTopicEmbedding(float[] newEmbedding)
    {
        // Average of recent N embeddings
        _recentEmbeddings.Enqueue(newEmbedding);
        if (_recentEmbeddings.Count > 5)
            _recentEmbeddings.Dequeue();

        _currentTopicEmbedding = AverageEmbeddings(_recentEmbeddings);
    }
}
```

### 7.2 Type Auto-Classification

```csharp
internal class TypeClassifier
{
    private readonly ITextCompletionService _llm;

    public async Task<MemoryType> ClassifyAsync(string content)
    {
        // 1. Fast heuristic patterns
        var heuristicResult = TryHeuristicClassify(content);
        if (heuristicResult.HasValue)
            return heuristicResult.Value;

        // 2. LLM classification (cached)
        return await LLMClassifyAsync(content);
    }

    private MemoryType? TryHeuristicClassify(string content)
    {
        var lower = content.ToLowerInvariant();

        // Semantic patterns
        if (Regex.IsMatch(lower, @"^(user |사용자 )?(is|are|prefers?|likes?|uses?)"))
            return MemoryType.Semantic;
        if (lower.Contains("confirmed:") || lower.Contains("ruled_out:"))
            return MemoryType.Semantic;

        // Episodic patterns
        if (lower.StartsWith("user:") || lower.StartsWith("assistant:"))
            return MemoryType.Episodic;
        if (Regex.IsMatch(lower, @"\b(yesterday|today|asked|said|discussed)\b"))
            return MemoryType.Episodic;

        // Procedural patterns
        if (Regex.IsMatch(lower, @"^(when|if|always|never|rule:|pattern:)"))
            return MemoryType.Procedural;
        if (lower.Contains("→") || lower.Contains("->"))
            return MemoryType.Procedural;

        return null;  // LLM classification needed
    }
}
```

---

## 8. Domain Profile System (v0.5.x)

### 8.1 DomainProfile Definition

```csharp
public sealed class DomainProfile
{
    public required string Name { get; init; }
    public TypeDistribution TypeHint { get; init; } = TypeDistribution.Balanced;
    public Scope MaxScope { get; init; } = Scope.User;
    public Tier MaxTier { get; init; } = Tier.Archive;
    public ExtractionConfig Extraction { get; init; } = new();
    public PromotionConfig Promotion { get; init; } = new();
}

public sealed class TypeDistribution
{
    public float EpisodicRatio { get; init; } = 0.33f;
    public float SemanticRatio { get; init; } = 0.34f;
    public float ProceduralRatio { get; init; } = 0.33f;
    public MemoryType PrimaryType { get; init; } = MemoryType.Semantic;

    public static TypeDistribution Balanced => new();
    public static TypeDistribution SemanticHeavy => new()
    {
        EpisodicRatio = 0.20f, SemanticRatio = 0.60f, ProceduralRatio = 0.20f
    };
}
```

### 8.2 Built-in Profiles

```csharp
public static class DomainProfiles
{
    public static DomainProfile Chat => new()
    {
        Name = "Chat",
        TypeHint = new() { SemanticRatio = 0.45f, EpisodicRatio = 0.30f, ProceduralRatio = 0.25f },
        MaxScope = Scope.User,
        MaxTier = Tier.Archive,
        Extraction = new() { AutoExtractFacts = true, AutoLearnPatterns = true }
    };

    public static DomainProfile TwentyQuestions => new()
    {
        Name = "TwentyQuestions",
        MaxScope = Scope.Session,    // No User scope
        MaxTier = Tier.Long,          // No Archive
        Extraction = new() { AutoExtractFacts = true }
    };

    public static DomainProfile RAG => new()
    {
        Name = "RAG",
        TypeHint = TypeDistribution.SemanticHeavy,
        MaxScope = Scope.User,
        MaxTier = Tier.Archive,
        Extraction = new() { AutoExtractFacts = true, AutoLearnPatterns = false }
    };
}
```

---

## 9. Implementation Phases

### Phase 32: 3-Axis Foundation (v0.4.1) — 3 weeks

**Goal**: Establish 3-axis model infrastructure

**Phase 32.1: Scope & Tier Enums** (Week 1)
- Create `Scope` enum (User/Session/Topic/Turn)
- Rename `MemoryTier` → `Tier` enum
- Rename tier values: Sensory→Buffer, Working→Short, Episodic→Long, Semantic→Archive
- Add `Scope`, `TopicId`, `Confidence`, `ConfirmCount`, `ActivationCount` to `MemoryUnit`
- Update all references to `MemoryTier` → `Tier`
- **Deliverable**: Enums + MemoryUnit schema updated

**Phase 32.2: Scope Promotion Logic** (Week 2)
- Implement `ScopePromoter` service (Turn→Topic→Session→User)
- Add Topic change detection (basic threshold-based, no auto-detection yet)
- Update `WorkingMemoryOrchestratorService` for Scope-aware promotion
- Add Scope-based filtering to `IMemoryStore` implementations
- **Deliverable**: Scope promotion engine working

**Phase 32.3: Tests & Documentation** (Week 3)
- Update 848 existing tests for Tier rename + Scope field
- Add Scope promotion tests (50+ tests)
- Update `TIER_TYPE_MATRIX.md` → `TIER_TYPE_SCOPE_MATRIX.md` (3D matrix)
- Update `ARCHITECTURE.md` with Scope axis
- Add migration notes to `MIGRATION_V0.4.md`
- **Deliverable**: All tests passing, docs updated

---

### Phase 33: Simple API (v0.4.2) — 2 weeks

**Goal**: User-facing Simple API for 99% use cases

**Phase 33.1: IMemoryService Interface** (Week 1)
- Create `IMemoryService` interface (Remember/Recall/EndSession/Forget)
- Create `MemoryContext` class with `ToPrompt()` method
- Implement `MemoryService` class (facade over existing VCM)
- Add Type auto-classification (heuristic only, no LLM yet)
- Register `IMemoryService` in DI
- **Deliverable**: Simple API functional

**Phase 33.2: Tests & Documentation** (Week 2)
- Add Simple API tests (80+ tests)
- Add chatbot sample app (`samples/SimpleChatBot/`)
- Update `QUICKSTART.md` with Simple API tutorial
- Add FAQ.md with "How to build chatbot" entry
- Update MCP tools to use Simple API internally
- **Deliverable**: Simple API production-ready

---

### Phase 34: Expert API (v0.5.0) — 2 weeks

**Goal**: Full control for games and special domains

**Phase 34.1: IMemoryServiceAdvanced** (Week 1)
- Create `IMemoryServiceAdvanced` interface (extends `IMemoryService`)
- Implement Type-specific methods (StoreFact/Event/Rule, RecallFacts/Events/Rules)
- Implement Full Control (StoreAsync/SearchAsync with full options)
- Add manual Scope/Tier promotion methods (PromoteAsync, ConvertTypeAsync)
- **Deliverable**: Expert API functional

**Phase 34.2: Tests & Documentation** (Week 2)
- Add Expert API tests (100+ tests)
- Update `TwentyQuestionsGame` sample to use Expert API
- Add API comparison guide (Level 0-3 decision tree)
- Update `GUIDES.md` with Expert API patterns
- **Deliverable**: Expert API production-ready

---

### Phase 35: Domain Profiles (v0.5.1) — 2 weeks

**Goal**: Domain-specific optimization presets

**Phase 35.1: DomainProfile System** (Week 1)
- Create `DomainProfile`, `TypeDistribution`, `ExtractionConfig`, `PromotionConfig` classes
- Implement built-in profiles (Chat, TwentyQuestions, RAG, CodingAssistant)
- Add `ApplyProfileAsync()` to `IMemoryServiceAdvanced`
- Add `MemoryIndexerOptions.DefaultProfile` setting
- **Deliverable**: Profile system working

**Phase 35.2: Tests & Documentation** (Week 2)
- Add DomainProfile tests (60+ tests)
- Add custom profile examples to docs
- Add RAG sample app (`samples/RagSystem/`)
- Document profile design patterns
- **Deliverable**: Profile system production-ready

---

### Phase 36: Automation & Optimization (v0.5.2) — 2 weeks

**Goal**: Full automation with manual override

**Phase 36.1: Topic Auto-Detection** (Week 1)
- Implement `TopicDetector` with embedding-based similarity
- Add configurable threshold (default 0.4)
- Integrate with `RememberAsync()` workflow
- Add manual override in Expert API
- **Deliverable**: Topic detection working

**Phase 36.2: Type Auto-Classification + Optimization** (Week 2)
- Add LLM-based Type classification (with caching)
- Optimize cross-scope search performance
- Add benchmarks (performance regression tests)
- Add observability metrics for automation accuracy
- **Deliverable**: Full automation production-ready

---

## 10. Configuration Schema

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "memory.db",
      "VectorDimensions": 1024
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "bge-m3",
      "Dimensions": 1024
    },
    "Completion": {
      "Provider": "OpenAI",
      "Model": "gpt-4o-mini"
    },
    "DefaultProfile": "Chat",
    "VCM": {
      "Buffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "Short": {
        "Capacity": 7,
        "DefaultTtlMinutes": 10,
        "TokenThreshold": 2048
      },
      "Long": {
        "MaxMemoriesPerUser": 10000,
        "DecayRate": 0.001
      },
      "Archive": {
        "MaxMemoriesPerUser": 1000,
        "MinConfidenceForPromotion": 0.8,
        "MinConfirmCountForPromotion": 2
      },
      "Topic": {
        "ChangeThreshold": 0.4,
        "AutoDetection": true
      }
    },
    "Extraction": {
      "AutoExtractFacts": true,
      "AutoLearnPatterns": true
    }
  }
}
```

---

## 11. Migration from v0.3.x

### 11.1 Breaking Changes

| Component | Change | Impact |
|-----------|--------|--------|
| `MemoryTier` enum | Renamed to `Tier`, values renamed | ALL code using enum |
| `MemoryUnit.Tier` | Property type renamed | Storage schema migration |
| API surface | New `IMemoryService`, old `MemoryService` still exists | Users can adopt gradually |
| Configuration | Renamed keys (Sensory→Buffer, etc.) | appsettings.json update |

### 11.2 Migration Strategy

**For v0.x = No backward compatibility needed**:

1. **Code Migration**:
   - Find/Replace: `MemoryTier` → `Tier`
   - Find/Replace: `MemoryTier.Sensory` → `Tier.Buffer`
   - Find/Replace: `MemoryTier.Working` → `Tier.Short`
   - Find/Replace: `MemoryTier.Episodic` → `Tier.Long`
   - Find/Replace: `MemoryTier.Semantic` → `Tier.Archive`

2. **Data Migration** (automatic on first run):
   ```csharp
   // MemoryStoreMigrator will auto-run on startup
   Tier = oldTier switch
   {
       0 => Tier.Buffer,
       1 => Tier.Short,
       2 => Tier.Long,
       3 => Tier.Archive,
       _ => Tier.Short
   };
   Scope = sessionId != null ? Scope.Session : Scope.User;
   ```

3. **Configuration Migration**:
   ```json
   // Old (v0.3.x)
   "SensoryBuffer": { ... }
   "WorkingMemory": { ... }

   // New (v0.4.x)
   "Buffer": { ... }
   "Short": { ... }
   ```

---

## 12. Success Criteria

### Phase 32
- [x] Scope enum created and integrated
- [x] Tier enum renamed with new values
- [x] MemoryUnit schema extended
- [x] Scope promotion logic working
- [x] All 848+ tests passing
- [x] Documentation updated

### Phase 33
- [ ] IMemoryService API implemented
- [ ] MemoryContext with ToPrompt() working
- [ ] Type auto-classification (heuristic) accurate
- [ ] Simple chatbot sample working
- [ ] 80+ new tests passing

### Phase 34
- [ ] IMemoryServiceAdvanced implemented
- [ ] Full Control API working
- [ ] Manual override methods functional
- [ ] TwentyQuestions using Expert API
- [ ] 100+ new tests passing

### Phase 35
- [ ] DomainProfile system working
- [ ] Built-in profiles (Chat, Game, RAG) functional
- [ ] Custom profile support
- [ ] RAG sample app working
- [ ] 60+ new tests passing

### Phase 36
- [ ] Topic auto-detection accurate (>80%)
- [ ] Type auto-classification with LLM accurate (>85%)
- [ ] Performance benchmarks green
- [ ] Full automation working with manual override

---

**Document End**

*Memory Indexer v0.4.x — 3-Axis Cognitive Memory System*
*Reference this design in all Phase 32-36 implementations*
