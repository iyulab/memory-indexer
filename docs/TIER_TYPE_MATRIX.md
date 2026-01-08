# 3-Axis Memory Model: Understanding Memory Organization

## Overview

Memory Indexer uses **three orthogonal dimensions** to organize memories:

1. **Type** (Memory Classification): WHAT kind of memory it is
   → Based on content classification (Episodic, Semantic, Procedural, Fact, Reflection)

2. **Scope** (Temporal Reach): HOW FAR the memory reaches
   → Based on temporal containment (Turn, Topic, Session, User)

3. **Tier** (Storage Layer): WHERE the memory is stored
   → Based on cognitive architecture (Buffer, Short, Long, Archive)

**Key Insight**: These dimensions are **independent** and **orthogonal**.
→ Any memory Type can exist in any Scope at any Tier.

## 3D Matrix: Type × Scope × Tier

```
Type × Scope × Tier = 5 × 4 × 4 = 80 possible combinations
```

### Tier × Type Matrix (any Scope)

```
                │ Episodic │ Semantic │ Procedural │ Fact │ Reflection
────────────────┼──────────┼──────────┼────────────┼──────┼────────────
Buffer (T0)     │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
Short (T1)      │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
Long (T2)       │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
Archive (T3)    │    ✓     │    ✓     │     ✓      │  ✓   │     ✓
```

### Scope × Tier Natural Alignment

```
                │ Buffer (T0) │ Short (T1) │ Long (T2) │ Archive (T3)
────────────────┼─────────────┼────────────┼───────────┼──────────────
Turn (S3)       │  Primary    │   Rare     │    -      │      -
Topic (S2)      │   Rare      │  Primary   │   Rare    │      -
Session (S1)    │    -        │   Rare     │  Primary  │    Rare
User (S0)       │    -        │    -       │   Rare    │   Primary
```

**All combinations are valid, but some are more natural than others.**

## Tier Dimension (Storage Layer)

### T0: Buffer (Atkinson-Shiffrin Sensory Memory)

**Purpose**: Raw conversation staging with async processing
**Lifetime**: 60s idle OR 500 tokens OR 3 turns (OR logic)
**Cognitive Basis**: Atkinson & Shiffrin (1968) multi-store model — sensory memory stage

**Characteristics**:
- Full conversation text preserved
- No summarization or compression
- Fast write, deferred processing
- Automatic promotion to Short when thresholds met

**Example Content**:
```
User: "I prefer dark mode for coding"
Assistant: "I'll remember your preference for dark mode"
User: "Also, I usually code in Python"
```

---

### T1: Short (Baddeley's Working Memory)

**Purpose**: Active context management with topic grouping
**Capacity**: 4-7 items (Miller's magical number 7±2)
**Lifetime**: 10min idle OR 2K tokens OR topic change (OR logic)
**Cognitive Basis**: Baddeley & Hitch (1974) working memory model — phonological loop + visuospatial sketchpad

**Characteristics**:
- Topic-grouped memory chunks
- Summarized and compressed
- Limited capacity with LRU eviction
- Active in current conversation context

**Example Content**:
```
Topic: "User Coding Preferences"
Summary: "User prefers dark mode and primarily codes in Python"
Extracted Facts: ["dark_mode_preference", "python_developer"]
```

---

### T2: Long (Tulving's Episodic Memory)

**Purpose**: Session experiences and temporal events
**Storage**: Vector database (SQLite-vec or Qdrant)
**Lifetime**: Indefinite with importance-based pruning
**Cognitive Basis**: Tulving (1972) episodic memory — autobiographical events with temporal context

**Characteristics**:
- Session summaries with timestamps
- Vector embeddings for semantic search
- Temporal context preserved
- Importance-weighted retrieval

**Example Content**:
```
Session ID: "2024-12-15-morning"
Summary: "Discussed Python development setup and dark mode preferences"
Timestamp: 2024-12-15T09:30:00Z
Importance: 0.7
Entities: ["Python", "dark mode", "VSCode"]
```

---

### T3: Archive (Tulving's Semantic Memory)

**Purpose**: Long-term knowledge and cross-session facts
**Promotion**: Confidence ≥ 0.8 AND Confirmations ≥ 3 (AND logic)
**Lifetime**: Permanent with periodic validation
**Cognitive Basis**: Tulving (1972) semantic memory — context-free factual knowledge

**Characteristics**:
- Key-value structured facts
- Confirmation tracking (multi-session validation)
- High-confidence threshold (conservative promotion)
- Cross-session consistency

**Example Content**:
```
Key: "preferred_language"
Value: "Python"
Confidence: 0.9
ConfirmationCount: 5
SourceSessions: ["2024-12-15-morning", "2024-12-16-afternoon", ...]
Category: Skill
```

## Type Dimension (Memory Classification)

### Episodic Type

**Definition**: Event-based memories with temporal and contextual details
**Examples**:
- "Yesterday we discussed authentication implementation"
- "During our last session, you asked about performance optimization"
- "Three days ago, you mentioned preferring VSCode over IntelliJ"

**Characteristics**:
- Contains WHO, WHAT, WHEN, WHERE context
- Tied to specific episodes/sessions
- Temporal references (yesterday, last week, during session X)
- Can decay or be consolidated over time

**Tier Distribution**:
- **Buffer**: Raw episodic conversation turns
- **Short**: Recent session context chunks
- **Long**: Archived session summaries (primary home)
- **Archive**: Repeatedly referenced past events ("user always mentions...")

---

### Semantic Type

**Definition**: Context-free factual knowledge and general truths
**Examples**:
- "User prefers Python for backend development"
- "User's timezone is UTC-5"
- "React is a JavaScript library for building UIs"

**Characteristics**:
- No specific temporal or spatial context
- General facts and knowledge
- Not tied to specific episodes
- High reusability across sessions

**Tier Distribution**:
- **Buffer**: Raw factual statements from conversation
- **Short**: Recently discussed facts
- **Long**: Facts extracted from sessions
- **Archive**: Confirmed long-term facts (primary home)

---

### Procedural Type

**Definition**: How-to knowledge, workflows, and step-by-step processes
**Examples**:
- "To deploy: 1) Run tests, 2) Build, 3) Push to staging, 4) Run smoke tests"
- "User's typical morning routine: Check emails → Review PRs → Write code"
- "Authentication flow: Login → Validate → Generate JWT → Store in cookie"

**Characteristics**:
- Sequential steps and procedures
- Action-oriented knowledge
- "How to" rather than "what is"
- Process and workflow patterns

**Tier Distribution**:
- **Buffer**: Raw workflow descriptions
- **Short**: Currently discussed procedures
- **Long**: Archived workflow discussions
- **Archive**: Confirmed standard operating procedures

---

### Fact Type

**Definition**: Simple atomic statements or data points
**Examples**:
- "User's name is John"
- "API key expires on 2025-01-15"
- "Database has 1.2M records"

**Characteristics**:
- Atomic, discrete information
- Can be true or false
- Often key-value structured
- High specificity

**Tier Distribution**:
- **Buffer**: Raw factual statements
- **Short**: Recently mentioned facts
- **Long**: Facts from past sessions
- **Archive**: Confirmed persistent facts (primary home)

## Concrete Examples: Types Across Tiers

### Example 1: Episodic Type Across All Tiers

**Scenario**: User discusses a past debugging session

**T0 (Buffer)**:
```
"Yesterday we spent 2 hours debugging the authentication issue"
Type: Episodic (temporal reference: "yesterday", event: "debugging session")
```

**T1 (Short)**:
```
Topic: "Recent Debugging History"
Content: "Yesterday's 2-hour auth debugging session"
Type: Episodic
```

**T2 (Long)**:
```
Session: "2024-12-14-debugging"
Summary: "2-hour authentication debugging, identified JWT expiration issue"
Timestamp: 2024-12-14T14:00:00Z
Type: Episodic
```

**T3 (Archive)**:
```
Key: "known_issue_pattern"
Value: "User frequently encounters JWT expiration issues during auth debugging"
Type: Episodic (pattern extracted from multiple episodic events)
ConfirmationCount: 4
```

---

### Example 2: Semantic Type Across All Tiers

**Scenario**: User states programming language preference

**T0 (Buffer)**:
```
"I prefer TypeScript for frontend development"
Type: Semantic (general preference, no temporal context)
```

**T1 (Short)**:
```
Topic: "User Tech Preferences"
Content: "TypeScript for frontend, Python for backend"
Type: Semantic
```

**T2 (Long)**:
```
Session: "2024-12-10-tech-discussion"
Extract: "User expressed TypeScript preference for frontend"
Type: Semantic (fact extracted from session)
```

**T3 (Archive)**:
```
Key: "preferred_frontend_language"
Value: "TypeScript"
Confidence: 0.95
Type: Semantic
```

---

### Example 3: Procedural Type Across All Tiers

**Scenario**: User describes deployment workflow

**T0 (Buffer)**:
```
"Our deploy process is: run tests, build, push to staging, smoke test, then production"
Type: Procedural (step-by-step workflow)
```

**T1 (Short)**:
```
Topic: "Deployment Workflow"
Content: "5-step deploy: test → build → staging → smoke → prod"
Type: Procedural
```

**T2 (Long)**:
```
Session: "2024-12-12-deployment-discussion"
Summary: "Documented team's 5-step deployment workflow with staging validation"
Type: Procedural
```

**T3 (Archive)**:
```
Key: "deployment_workflow"
Value: "1. Tests 2. Build 3. Staging deploy 4. Smoke tests 5. Production"
Category: Workflow
Type: Procedural
ConfirmationCount: 3
```

---

### Example 4: Fact Type Across All Tiers

**Scenario**: User mentions team size

**T0 (Buffer)**:
```
"Our team has 12 developers"
Type: Fact (atomic data point)
```

**T1 (Short)**:
```
Topic: "Team Information"
Content: "Team: 12 developers, 3 designers, 2 PMs"
Type: Fact
```

**T2 (Long)**:
```
Session: "2024-12-08-team-overview"
Extract: "Team composition: 12 devs, 3 designers, 2 PMs (17 total)"
Type: Fact
```

**T3 (Archive)**:
```
Key: "team_size_developers"
Value: "12"
LastUpdated: 2024-12-08
Type: Fact
```

---

### Example 5: Mixed Types in Single Session

**Conversation**:
```
User: "Yesterday I implemented the OAuth flow [Episodic]
      using the standard 3-step process: [Procedural]
      1) Request token, 2) Validate, 3) Store.
      I prefer JWT over sessions [Semantic]
      because our API handles 10K requests/sec [Fact]."
```

**Buffer (T0)**:
- Stores entire message as-is
- Preserves all 4 types in raw form

**Short (T1)** — Topic Segmentation:
```
Topic 1: "Recent OAuth Implementation" [Episodic + Procedural]
Topic 2: "Auth Preferences" [Semantic + Fact]
```

**Long (T2)** — Session Summary:
```
Session: "oauth-implementation-2024-12-15"
Events: [Episodic] "Implemented OAuth yesterday"
Workflows: [Procedural] "3-step OAuth: request → validate → store"
Facts: [Fact] "API: 10K req/sec throughput"
Insights: [Semantic] "User prefers JWT auth"
```

**Archive (T3)** — Extracted Knowledge:
```
Entry 1: [Semantic]
  Key: "auth_preference"
  Value: "JWT over sessions"

Entry 2: [Procedural]
  Key: "oauth_workflow"
  Value: "1. Request token, 2. Validate, 3. Store"

Entry 3: [Fact]
  Key: "api_throughput"
  Value: "10000 req/sec"
```

## Memory Lifecycle: Type + Tier Evolution

### Example Lifecycle: "User prefers dark mode"

**Stage 1: Initial Capture (T0 Buffer)**
```
Timestamp: 2024-12-15 09:00:00
Content: "I prefer dark mode for coding"
Type: Semantic
Tier: T0 (Buffer)
```

**Stage 2: Topic Grouping (T1 Short)**
```
Timestamp: 2024-12-15 09:01:05 (60s trigger)
Topic: "User Preferences"
Content: "Dark mode for coding"
Type: Semantic
Tier: T1 (Short)
Promotion Reason: IdleTimeout (60s)
```

**Stage 3: Session Archive (T2 Long)**
```
Timestamp: 2024-12-15 09:15:00 (10min trigger)
Session: "preferences-discussion-2024-12-15"
Extract: "User stated dark mode preference for coding environments"
Type: Semantic (extracted from episodic session)
Tier: T2 (Long)
Promotion Reason: Short TTL expired
Importance: 0.6
```

**Stage 4: Confirmed Knowledge (T3 Archive)**
```
Timestamp: 2024-12-20 14:30:00 (after 3rd mention)
Key: "ui_theme_preference"
Value: "dark_mode"
Type: Semantic
Tier: T3 (Archive)
Promotion Reason: Confidence=0.85, ConfirmationCount=3
SourceSessions: [
  "preferences-discussion-2024-12-15",
  "ide-setup-2024-12-17",
  "vscode-config-2024-12-20"
]
```

**Type Unchanged, Tier Evolved**: Semantic → Semantic → Semantic → Semantic
**Tier Journey**: T0 → T1 → T2 → T3

---

### Example Lifecycle: "Yesterday's bug investigation"

**Stage 1: Initial Capture (T0 Buffer)**
```
Content: "Yesterday we spent 3 hours tracking down the race condition in the payment processor"
Type: Episodic
Tier: T0
```

**Stage 2: Working Context (T1 Short)**
```
Topic: "Recent Bug Investigations"
Content: "3-hour race condition debugging in payment processor (yesterday)"
Type: Episodic
Tier: T1
```

**Stage 3: Session Memory (T2 Long)**
```
Session: "payment-race-condition-2024-12-14"
Summary: "Investigated and resolved race condition in payment processor after 3-hour debug session"
Type: Episodic (primary home for episodic type)
Tier: T2
Importance: 0.8 (significant debugging effort)
```

**Stage 4: Pattern Extraction (T3 Archive)**
```
Key: "common_issue_payment_race_conditions"
Value: "Payment processor prone to race conditions under high load"
Type: Semantic (generalized knowledge from episodic events)
Tier: T3
ConfirmationCount: 3 (extracted from 3 separate debugging sessions)
```

**Type Evolution**: Episodic → Episodic → Episodic → Semantic (abstracted)
**Tier Journey**: T0 → T1 → T2 → T3

## Common Misconceptions

### ❌ Wrong: "Long only contains Episodic type"

**Counterexample**:
```
Long can contain:
- Episodic: "User asked about auth on 2024-12-10"
- Semantic: "User prefers REST over GraphQL" (extracted from session)
- Procedural: "Deployment workflow: test → build → deploy"
- Fact: "Database has 1.2M users" (mentioned in session)
```

**Why?** Long (Tier) stores SESSION MEMORIES. Those sessions can discuss ANY TYPE of content.

---

### ❌ Wrong: "Archive only contains Semantic type"

**Counterexample**:
```
Archive can contain:
- Semantic: "User's timezone is UTC-5" [Most common]
- Episodic: "User always starts meetings 5min late" [Pattern]
- Procedural: "User's code review process: 1) Format 2) Test 3) Review"
- Fact: "User's GitHub username is @johndoe"
```

**Why?** Archive (Tier) stores LONG-TERM CONFIRMED KNOWLEDGE. Any repeatedly confirmed knowledge qualifies, regardless of Type.

---

### ❌ Wrong: "Working Memory can only hold recent things"

**Counterexample**:
```
Short can hold:
- Recent conversation context (yes, most common)
- Retrieved historical context (recalled from Long for current task)
- Long-term facts (fetched from Archive for current topic)
```

**Why?** Short (Tier) represents ACTIVE CONTEXT. Retrieval can pull ANY type from ANY tier into Working for immediate use.

---

### ❌ Wrong: "Types determine promotion between tiers"

**Correct**: Promotion is based on TIER thresholds, NOT types.

**Promotion Triggers**:
- **T0 → T1**: Time (60s) OR Tokens (500) OR Turns (3) [OR logic]
- **T1 → T2**: Time (10min) OR Tokens (2K) OR Turns (10) OR Topic Change [OR logic]
- **T2 → T3**: Confidence (≥0.8) AND Confirmations (≥3) [AND logic]

**Type-Agnostic**: All types follow the same promotion rules within each tier.

---

### ❌ Wrong: "Once in Archive, memories never change tier"

**Correct**: Retrieval can bring Archive entries back to Short.

**Example Flow**:
```
1. Archive: "preferred_language: Python" (T3, confirmed)
2. User asks: "What's my preferred language?" (retrieval query)
3. Short: Add "User prefers Python" to active context (T3 → T1)
4. Response uses Short content
5. After session ends, returns to Archive (T1 → T3)
```

**Why?** Tiers represent CURRENT LOCATION and LIFECYCLE STAGE, not permanent residence.

---

### ❌ Wrong: "Procedural memories must be in Archive"

**Counterexample**:
```
Procedural in Buffer:
  "To deploy: run tests, build, push" (raw instruction)

Procedural in Short:
  Currently discussing deployment workflow

Procedural in Long:
  Session about deployment procedure discussion

Procedural in Archive:
  Confirmed standard deployment SOP
```

**Why?** Procedural TYPE describes content nature (how-to). It can exist at ANY tier based on lifecycle stage.

## API Usage: Demonstrating Tier/Type Separation

### Storing with Explicit Type, Implicit Tier

```csharp
// Store Semantic fact - starts in Buffer (T0)
await memoryService.StoreAsync(
    userId: "user123",
    content: "User prefers TypeScript",
    type: MemoryType.Semantic,  // Type classification
    importance: 0.7
);
// Tier: T0 (Buffer) - automatic assignment
// Type: Semantic - explicitly specified
```

### Filtering by Type Across All Tiers

```csharp
// Recall all Procedural memories regardless of tier
var procedures = await memoryService.RecallAsync(
    userId: "user123",
    query: "deployment workflow",
    filterOptions: new MemoryFilterOptions
    {
        Types = [MemoryType.Procedural]  // Type filter
        // No tier filter - searches ALL tiers
    }
);
// Returns Procedural memories from T0, T1, T2, T3
```

### Filtering by Tier Across All Types

```csharp
// Get current working memory (all types)
var workingContext = await memoryService.RecallAsync(
    userId: "user123",
    query: "*",  // All content
    filterOptions: new MemoryFilterOptions
    {
        Tiers = [MemoryTier.Working]  // Tier filter
        // No type filter - all types included
    }
);
// Returns all Types (Episodic, Semantic, Procedural, Fact) from T1 only
```

### Filtering by Both Tier and Type

```csharp
// Get confirmed semantic knowledge only
var confirmedFacts = await memoryService.RecallAsync(
    userId: "user123",
    query: "user preferences",
    filterOptions: new MemoryFilterOptions
    {
        Tiers = [MemoryTier.Semantic],      // T3 only
        Types = [MemoryType.Semantic]      // Semantic type only
    }
);
// Returns: T3 × Semantic intersection
```

### Direct Tier Access

```csharp
// Access Buffer directly (T0)
var sensoryBuffer = serviceProvider.GetRequiredService<IBuffer>();
await sensoryBuffer.EnqueueAsync("Raw conversation text", "user123");

// Access Short directly (T1)
var workingMemory = serviceProvider.GetRequiredService<IShort>();
var activeContext = workingMemory.GetAll();  // All types in T1

// Access Long directly (T2)
var episodicStore = serviceProvider.GetRequiredService<ILong>();
var sessionMemories = await episodicStore.GetSessionsAsync("user123");

// Access Archive directly (T3)
var semanticStore = serviceProvider.GetRequiredService<IArchive>();
var userProfile = await semanticStore.GetAllAsync("user123");
```

## Tier × Type: Design Principles

### Orthogonality Principle

**Definition**: Tier and Type are independent dimensions.

**Benefits**:
1. **Flexibility**: Any content type can exist at any storage layer
2. **Simplicity**: Two separate concerns, easier to reason about
3. **Scalability**: Tier policies independent of Type policies
4. **Composability**: Filter by Tier × Type combinations as needed

### Tier-Based Lifecycle

**Principle**: Promotion is tier-driven, not type-driven.

**Promotion Logic**:
- **Buffer → Short**: Time/Token/Turn thresholds (OR)
- **Short → Long**: Time/Token/Turn/Topic thresholds (OR)
- **Long → Archive**: Confidence + Confirmation thresholds (AND)

**Type-Agnostic**: All types follow the same promotion rules.

### Type-Based Classification

**Principle**: Type describes content nature, not storage location.

**Classification Logic**:
- **Episodic**: Contains temporal/contextual event details
- **Semantic**: Context-free facts and knowledge
- **Procedural**: How-to processes and workflows
- **Fact**: Atomic discrete data points

**Tier-Agnostic**: Classification independent of current tier.

### Separation of Concerns

**Tier Dimension Concerns**:
- WHERE memory is stored (physical/logical location)
- WHEN memory is promoted (lifecycle management)
- HOW LONG memory persists (TTL and retention)

**Type Dimension Concerns**:
- WHAT kind of content (classification)
- HOW to interpret content (semantic meaning)
- WHY content matters (relevance and importance)

## Summary

**Remember**:
1. **Tier** = Storage layer (WHERE + WHEN)
2. **Type** = Content classification (WHAT + WHY)
3. **Orthogonal** = Independent dimensions
4. **All combinations valid** = Any Type in any Tier
5. **Lifecycle** = Tier-driven promotion
6. **Classification** = Type-driven interpretation

**Mental Model**:
- Think of Tier as the "container" (cup, bucket, tank, reservoir)
- Think of Type as the "liquid" (water, juice, milk, soda)
- Any liquid can go in any container
- Container size/rules determine promotion (overflow → next container)
- Liquid type determines how it's used (drink, cook, clean)

**Next Steps**:
- See `ARCHITECTURE.md` for detailed tier specifications
- See `VISION.md` for cognitive science foundations
- See `GUIDES.md` for common usage patterns
