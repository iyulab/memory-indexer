# Memory Indexer

**Cognitive Memory System for LLMs** — An MCP server implementing human-inspired memory architecture with 3-Tier Virtual Context Management.

[![CI](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MemoryIndexer?logo=nuget)](https://www.nuget.org/packages/MemoryIndexer)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

## Vision

LLMs face a fundamental constraint: **finite context windows**. Memory Indexer solves this by implementing a cognitive architecture inspired by human memory systems—where forgetting is not a bug, but a feature.

> *"The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."*
> — Richards & Frankland (2017), "The Persistence and Transience of Memory"

## Architecture

### 3-Tier Virtual Context Management (VCM)

Memory Indexer operates like an **operating system for LLM memory**, implementing virtual memory paging between three tiers:

```
┌─────────────────────────────────────────────────────────────────┐
│  L1: Working Memory (In-Context)                                │
│  ├─ Capacity: 4-7 chunks (Baddeley's Working Memory Model)      │
│  ├─ Latency: ~microseconds                                      │
│  ├─ Storage: IMemoryCache                                       │
│  └─ Scope: Current task context                                 │
├─────────────────────────────────────────────────────────────────┤
│  L2: Session Memory                                             │
│  ├─ Capacity: Session-scoped                                    │
│  ├─ Latency: ~milliseconds                                      │
│  ├─ Storage: Vector DB (Qdrant/SQLite-vec)                      │
│  └─ Scope: Current conversation session                         │
├─────────────────────────────────────────────────────────────────┤
│  L3: User Memory (Long-term)                                    │
│  ├─ Capacity: Unlimited                                         │
│  ├─ Latency: ~milliseconds to seconds                           │
│  ├─ Storage: Hybrid (Vector + Knowledge Graph)                  │
│  └─ Scope: Cross-session persistent knowledge                   │
└─────────────────────────────────────────────────────────────────┘
```

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
| **Promote** | Move to higher tier (L2→L1) | Page-In |
| **Demote** | Move to lower tier (L1→L2) | Page-Out |
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

**Spacing Effect**: Repeated access increases stability:
- 2+ accesses → Stabilizing
- 5+ accesses → Stable
- 10+ accesses → Consolidated

## What's New in v0.2.0

- **Hybrid Scoring**: Keyword matching combined with content-type boosting for improved recall
- **CONFIRMED Memory Priority**: Positive/confirmed information ranks higher than ruled-out content
- **TwentyQuestionsGame Sample**: Demonstrates memory-only context (no chat history passed to LLM)

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

### Knowledge Graph Integration

Temporal knowledge tracking with relation management:

```csharp
// Fact supersession chain
Memory["CEO of Apple"] = "Tim Cook"  // SupersedesId: null
Memory["CEO of Apple"] = "New CEO"   // SupersedesId: previous memory ID
```

## Installation

### As MCP Server

```bash
# Clone repository
git clone https://github.com/iyulab/memory-indexer.git
cd memory-indexer

# Build
dotnet build

# Run MCP server
dotnet run --project src/MemoryIndexer.Console
```

### Claude Desktop Configuration

Add to `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "memory-indexer": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/MemoryIndexer.Console"]
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
      "WorkingMemoryCapacity": 7,
      "EvictionThreshold": 0.1,
      "ConsolidationInterval": "01:00:00"
    },
    "Search": {
      "DefaultLimit": 10,
      "RRFConstant": 60,
      "EnableDAT": true
    }
  }
}
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

## Project Structure

```
src/
├── MemoryIndexer.Core/          # Domain models and interfaces
│   ├── Models/                  # MemoryUnit, MemoryTier, enums
│   ├── Interfaces/              # IMemoryStore, IVirtualContextManager
│   └── Services/                # MemoryService orchestration
├── MemoryIndexer.Storage/       # Storage implementations
│   ├── InMemory/                # Development/testing
│   ├── Sqlite/                  # SQLite-vec persistent storage
│   └── Qdrant/                  # Production vector database
├── MemoryIndexer.Intelligence/  # AI/ML integrations
│   ├── Embedding/               # BGE-M3 via Ollama/LMSupply
│   ├── Reranking/               # LMSupply.Reranker integration
│   └── Classification/          # Memory type classifier
├── MemoryIndexer.Mcp/           # MCP protocol layer
│   └── Tools/                   # MCP tool implementations
├── MemoryIndexer.Console/       # CLI entry point
└── MemoryIndexer.Sdk/           # NuGet package for embedding
```

## Success Metrics

Based on research benchmarks:

| Metric | Target | Description |
|--------|--------|-------------|
| Memory Reuse Rate | ≥58.6% | Ratio of retrieved vs stored memories |
| Net Efficiency Gain | 17-18% | Task completion improvement |
| Context Utilization | <85% | Avoid fragility tipping point |
| Retrieval Latency | <100ms | P95 search response time |

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Implementation Plan](local-docs/IMPLEMENTATION_PLAN.md)
- [Refactoring Plan](local-docs/REFACTORING_PLAN_V2.md)

## License

MIT

---

*"Memory is not about the past. It's about the future."* — Endel Tulving
