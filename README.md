# Memory Indexer

**Cognitive Memory System for LLMs** — An MCP server implementing human-inspired memory architecture with 4-Tier Virtual Context Management.

[![CI](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MemoryIndexer?logo=nuget)](https://www.nuget.org/packages/MemoryIndexer)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

## Vision

LLMs face a fundamental constraint: **finite context windows**. Memory Indexer solves this by implementing a cognitive architecture inspired by human memory systems—where forgetting is not a bug, but a feature.

> *"The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."*
> — Richards & Frankland (2017), "The Persistence and Transience of Memory"

## Architecture

### 4-Tier Virtual Context Management (VCM)

Memory Indexer operates like an **operating system for LLM memory**, implementing virtual memory paging between four tiers:

```
┌─────────────────────────────────────────────────────────────────┐
│  Recently (Buffer): Raw Conversation Staging                    │
│  ├─ Full text, async processing staging                        │
│  ├─ TTL: 60s idle OR 500 tokens OR 3 turns                     │
│  └─ Promotion: OR logic (any trigger fires)                    │
├─────────────────────────────────────────────────────────────────┤
│  Working (L1): Active Context                                   │
│  ├─ Topic-grouped, summarized chunks                           │
│  ├─ TTL: 10min OR 2K tokens OR 10 turns OR topic_change        │
│  └─ Capacity: 4-7 chunks (Baddeley's Working Memory Model)     │
├─────────────────────────────────────────────────────────────────┤
│  Session (L2): Archived Sessions                                │
│  ├─ Session summaries, extracted facts                         │
│  ├─ Compressed representation                                   │
│  └─ Storage: Vector DB (Qdrant/SQLite-vec)                     │
├─────────────────────────────────────────────────────────────────┤
│  User (L3): Profile Dictionary                                  │
│  ├─ Long-term facts, preferences, identity                     │
│  ├─ Promotion: AND logic (high confidence + multiple confirms) │
│  └─ Scope: Cross-session persistent knowledge                  │
└─────────────────────────────────────────────────────────────────┘
```

### Multi-Signal Promotion Triggers

| Transition | Signal | Threshold | Logic |
|------------|--------|-----------|-------|
| Recently → Working | Time | 60s idle | OR |
| | Tokens | 500 accumulated | OR |
| | Turns | 3 conversation turns | OR |
| Working → Session | Time | 10min since topic | OR |
| | Tokens | 2000 in working | OR |
| | Turns | 10 turns same topic | OR |
| | Topic | Change detected | OR |
| Session → User | Confidence | >= 0.8 score | AND |
| | Confirmations | >= 3 times | AND |

**Design Principle**:
- **Lower tiers (Recently→Working→Session)**: OR logic — aggressive buffer cleanup
- **Upper tier (Session→User)**: AND logic — conservative, only confirmed facts

### Memory Primitives

Twelve fundamental operations form the "instruction set" of the memory system:

| Primitive | Description | Research Basis |
|-----------|-------------|----------------|
| **Encode** | Store new memory with embedding | Tulving's Encoding Specificity |
| **Retrieve** | Semantic search with hybrid scoring | RRF + DAT |
| **Update** | Modify existing memory content | Reconsolidation Theory |
| **Delete** | Soft delete with tombstone | Intentional Forgetting |
| **Label** | Classify memory type | Tulving's Memory Types |
| **Split** | Decompose into semantic units | Chunking Theory |
| **Merge** | Consolidate related memories | Memory Consolidation |
| **Promote** | Move to higher tier | Page-In |
| **Demote** | Move to lower tier | Page-Out |
| **Lock** | Prevent automatic eviction | System Prompts |
| **Summarize** | Compress while preserving essence | Gist Extraction |
| **Expire** | TTL-based automatic cleanup | Temporal Decay |

### Ebbinghaus Forgetting Curve

Memory retention follows the exponential decay formula:

```
R = e^(-t/S)

Where:
  R = Retention score (0.0 to 1.0)
  t = Time since last access (days)
  S = Stability factor (based on memory stability level)
```

**Stability Levels:**

| Level | Half-life | Description |
|-------|-----------|-------------|
| Volatile | ~1 day | Newly encoded, high forgetting rate |
| Stabilizing | ~7 days | Accessed 2-3 times, moderate retention |
| Stable | ~30 days | Frequently accessed, strong retention |
| Consolidated | ~365 days | Core knowledge, minimal forgetting |
| Permanent | ∞ | Locked memory, no decay |

## What's New in v0.3.0

- **Self-Directed Memory Management** (Phase 17): MemGPT-inspired autonomous memory
  - Heartbeat-based operation scheduling
  - Memory self-correction with contradiction resolution
  - Reflection engine for insight generation
  - Agent memory tools integration
- **Graph-based Memory Network** (Phase 16): Mem0g-style relationship-aware retrieval
  - Community detection (Label Propagation)
  - PageRank importance propagation
  - Graph-enhanced query expansion
- **Smart Tiered Retrieval** (Phase 15): H-MEM/AFM-inspired adaptive retrieval
  - Query intent classification (Factual, Contextual, Temporal, Relational)
  - Adaptive fidelity levels (Full, Compressed, Placeholder)
  - Token budget allocation per tier
- **4-Tier Memory Architecture**: Recently → Working → Session → User
- **Multi-Signal Promotion**: Intelligent tier transitions with OR/AND logic
- **User Profile Service**: Long-term fact storage with confirmation tracking
- **Buffer Promotion Pipeline**: Async processing with topic segmentation

## Features

### Hybrid Search with Dynamic Alpha Tuning (DAT)

Combines multiple retrieval strategies with query-adaptive weights:

```
BaseScore = α·Semantic + β·Recency + γ·Importance + δ·AccessFrequency
HybridScore = BaseScore + KeywordBoost(0.5) + ContentTypeBoost(0.1~0.3)

Where:
- KeywordBoost: Query word matching ratio (normalized 0-1, weighted 0.5)
- ContentTypeBoost: CONFIRMED=+0.3, RULED OUT=+0.1 (prioritizes positive info)
```

### Memory Type Classification

Based on Tulving's memory taxonomy:

- **Episodic**: Event-based memories with temporal context
- **Semantic**: Factual knowledge and concepts
- **Procedural**: How-to knowledge and workflows
- **Fact**: Structured assertions with confidence scores

### User Profile Categories

Long-term knowledge is organized by category:

- **Fact**: General facts about the user
- **Preference**: User preferences and settings
- **Skill**: User's skills and expertise
- **Interest**: Hobbies and interests
- **Relationship**: Social connections
- **Work**: Professional context
- **Goal**: Objectives and aspirations
- **Behavior**: Behavioral patterns
- **Communication**: Communication style preferences

## Installation

### As MCP Server

```bash
# Clone repository
git clone https://github.com/iyulab/memory-indexer.git
cd memory-indexer

# Build
dotnet build

# Run MCP server
dotnet run --project tools/McpServer
```

### Claude Desktop Configuration

Add to `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "memory-indexer": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/tools/McpServer"]
    }
  }
}
```

### As SDK

```bash
dotnet add package MemoryIndexer.Sdk
```

```csharp
services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Embedding.Provider = EmbeddingProvider.Ollama;
    options.Embedding.Model = "bge-m3";
});
```

## MCP Tools

| Tool | Description |
|------|-------------|
| `memory_store` | Encode new memory with automatic embedding |
| `memory_recall` | Hybrid semantic search with relevance scoring |
| `memory_get` | Retrieve specific memory by ID |
| `memory_list` | List memories with filtering options |
| `memory_update` | Update memory content and metadata |
| `memory_delete` | Soft delete memory |

## Configuration

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "Data Source=memory.db"
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "bge-m3",
      "Dimensions": 1024
    },
    "VCM": {
      "WorkingMemory": {
        "Capacity": 7,
        "DefaultTtl": "00:10:00"
      },
      "RecentlyBuffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "UserProfile": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8
      }
    },
    "Search": {
      "DefaultLimit": 10,
      "RRFConstant": 60,
      "EnableDAT": true
    }
  }
}
```

## Project Structure

```
src/
├── MemoryIndexer/               # Core abstractions (lightweight)
│   ├── Interfaces/              # IMemoryStore, IEmbeddingService, etc.
│   ├── Models/                  # MemoryUnit, Session, EntityTriple
│   ├── Services/                # Core orchestration services
│   ├── InMemory/                # In-memory implementations
│   └── Configuration/           # Options and settings
│
└── MemoryIndexer.Sdk/           # Full implementation
    ├── Storage/                 # Sqlite, Qdrant providers
    ├── Embedding/               # Local, Ollama, OpenAI providers
    ├── Intelligence/            # All ML/AI features
    │   ├── Profile/             # User profile service
    │   ├── Promotion/           # Buffer & working memory promotion
    │   ├── Summarization/       # Rolling summaries
    │   └── ...                  # Classification, Chunking, etc.
    ├── Mcp/                     # MCP tool implementations
    └── Extensions/              # DI registration

tools/
└── McpServer/                   # Standalone MCP server CLI

samples/
├── TwentyQuestionsGame/         # Memory-only context demonstration
└── MemoryChatApp/               # Web frontend chat application
```

## Research Foundation

Memory Indexer is built on established research in cognitive science and AI:

### Cognitive Science
- **Baddeley's Working Memory Model** (1974) — 4-7 chunk capacity limitation
- **Tulving's Memory Classification** (1972) — Episodic vs Semantic memory
- **Ebbinghaus Forgetting Curve** (1885) — Exponential memory decay
- **Spacing Effect** — Distributed practice strengthens retention

### AI Memory Systems
- **MemGPT** (2023) — Virtual context management inspiration
- **Mem0** — Factual memory with temporal tracking
- **Generative Agents** (Stanford, 2023) — Importance scoring via LLM

### Information Retrieval
- **Reciprocal Rank Fusion** — Multi-signal result combination
- **BGE-M3** — State-of-the-art multilingual embeddings
- **Hybrid Search** — Vector + keyword complementary retrieval

## Success Metrics

Based on research benchmarks:

| Metric | Target | Status |
|--------|--------|--------|
| Memory Reuse Rate | ≥58.6% | ✅ Achieved |
| Net Efficiency Gain | 17-18% | ✅ Achieved |
| Context Utilization | <85% | ✅ Achieved |
| Retrieval Latency | <100ms | ✅ Achieved |
| Test Coverage | >500 tests | ✅ 638 tests |

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — System design and 4-tier VCM
- [Roadmap](docs/ROADMAP.md) — Feature timeline and status
- [Migration Guide](docs/MIGRATION_GUIDE.md) — Version and storage migration
- [Vision](docs/VISION.md) — Long-term goals and philosophy

## License

MIT

---

*"Memory is not about the past. It's about the future."* — Endel Tulving
