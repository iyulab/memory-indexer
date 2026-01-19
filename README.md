# Memory Indexer

A cognitive memory system for LLMs implementing human-inspired 3-axis memory architecture.

[![CI](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml)
[![Tests](https://img.shields.io/badge/tests-1549-success?logo=testcafe)](tests/)
[![NuGet](https://img.shields.io/nuget/v/MemoryIndexer?logo=nuget)](https://www.nuget.org/packages/MemoryIndexer)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

## The Problem

LLMs face a fundamental constraint: **finite context windows**.

```
┌─────────────────────────────────────────────────────┐
│  Session 1  │  Session 2  │  Session 3  │  Current  │
│   (lost)    │   (lost)    │   (lost)    │  (active) │
└─────────────────────────────────────────────────────┘
```

**Current workarounds fall short:**

| Approach | Limitation |
|----------|------------|
| Summarization | Information loss, extra LLM calls |
| Sliding Window | Important early context lost |
| Full History | Hits token limits quickly |
| RAG | Not optimized for conversation context |

## The Solution

Memory Indexer provides **Zero Context Engineering**—you focus on your prompt, we handle all memory management.

**Before** (manual context management):
```python
class ChatService:
    def chat(self, message):
        # You manage: history, summarization, token counting,
        # context assembly, profile loading, fact extraction...
        if self.count_tokens(self.history) > MAX_TOKENS:
            self.history = self.summarize(self.history)  # 😓
```

**After** (with Memory Indexer):
```python
class ChatService:
    def chat(self, message):
        await memory.store(session, message)           # Auto-classify, auto-place
        context = await memory.recall(message)         # Intelligent retrieval
        return await llm.generate(context, message)    # Done.
```

> *"The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."*
> — Richards & Frankland (2017)

## Role & Scope

| What It Is | What It Isn't |
|------------|---------------|
| General-purpose memory primitives | A chatbot framework |
| Cognitive science-based architecture | A vector database |
| MCP server for any LLM client | Tied to specific use cases |
| Domain-agnostic building blocks | An opinionated application |

## Core Architecture

**3-Axis Memory Model** where each memory has three orthogonal dimensions:

```
Type × Scope × Tier = What × When × Where
```

| Axis | Values | Cognitive Basis |
|------|--------|-----------------|
| **Type** | Episodic, Semantic, Procedural, Fact, Reflection | Tulving's memory classification |
| **Scope** | Turn, Topic, Session, User | Temporal reach (seconds → forever) |
| **Tier** | Buffer, Short, Long, Archive | Atkinson-Shiffrin + Baddeley |

```
Tier Promotion Pipeline (Atkinson-Shiffrin + Tulving):

┌─────────────────────────────────────────────────────┐
│  Buffer (T0) - Sensory Store                        │
│  TTL: 60s idle │ 500 tokens │ 3 turns               │
├─────────────────────────────────────────────────────┤
│  Short (T1) - Working Memory (Baddeley's 7±2)       │
│  Capacity: 9 items, auto-promote when exceeded      │
├─────────────────────────────────────────────────────┤
│  Long (T2) - Episodic Memory                        │
│  Session-level events and experiences               │
├─────────────────────────────────────────────────────┤
│  Archive (T3) - Semantic Memory                     │
│  Promotion: Confidence ≥ 0.8 AND Confirms ≥ 3       │
└─────────────────────────────────────────────────────┘
```

## Benchmark Summary

| Operation | Latency | Throughput |
|-----------|---------|------------|
| Store | ~2.3 μs | 435K ops/s |
| Recall (limit 5) | ~1.5 μs | 667K ops/s |
| Store→Recall workflow | ~3.8 μs | 263K ops/s |

> In-memory storage with mock embeddings. See [Benchmark Details](docs/BENCHMARKS.md) for full results.

## Quick Start

### As MCP Server

```bash
dotnet tool install -g MemoryIndexer.Mcp
```

Configure Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "memory-indexer": {
      "command": "memory-indexer-mcp"
    }
  }
}
```

### As SDK

```bash
dotnet add package MemoryIndexer.Sdk
```

```csharp
// Register your embedding service BEFORE AddMemoryIndexer()
services.AddSingleton<IEmbeddingService>(myEmbeddingService);

// InMemory storage (default)
services.AddMemoryIndexer(options =>
{
    options.Embedding.Dimensions = 1536;  // Match your embedding model
});

// Or with SQLite persistent storage
services.AddMemoryIndexer(options =>
{
    options.Storage.ConnectionString = "memories.db";
    options.Embedding.Dimensions = 1536;
}).WithSqliteVec();

// Store
await memoryService.StoreAsync("user123", "User prefers dark mode", importance: 0.8f);

// Recall
var results = await memoryService.RecallAsync("user123", "UI preferences", limit: 5);
```

## Samples

### [MemoryChatApp](samples/MemoryChatApp/)

Web-based chat demonstrating **Context Budget API**—intelligent recall replaces full conversation history.

```
Traditional: messages = [msg1, msg2, ... msgN]  → Token cost: O(n)
This Demo:   context = recall(query, budget=2000)  → Token cost: O(1)
```

**Features:**
- Token-budget-aware context building (RecentHeavy, Balanced, SemanticHeavy strategies)
- 4-tier memory visualization (Buffer → Short → Long → Archive)
- Session isolation with cross-session user facts
- Flexible embeddings (inject your own IEmbeddingService) with LLM support (GpuStack/OpenAI)

```bash
cd samples/MemoryChatApp
.\start-dev.ps1               # Opens frontend + backend
```

### [Twenty Questions Game](samples/TwentyQuestionsGame/)

AI vs AI demo where two LLM agents play 20 Questions using **only memory recall**—no chat history injection.

```
Traditional: messages: [Q1, A1, Q2, A2, ... Q19, A19]  ← O(n) growing context
This Demo:   user: "Alpha says: Yes"                   ← O(1) constant context
```

**What It Proves:**
- Agents build coherent multi-turn strategy via `memory_recall()` only
- O(1) context maintenance regardless of conversation length
- Memory isolation between agents works correctly

```bash
cd samples/TwentyQuestionsGame
dotnet run                    # Auto-detect LLM provider
dotnet run -- --local         # Use local ONNX model (no API key)
```

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/ARCHITECTURE.md) | System design, 3-axis model, tier/type details |
| [Intelligence](docs/INTELLIGENCE.md) | Conflict resolution, adaptive retrieval, graph traversal |
| [Evaluation](docs/EVALUATION.md) | KPIs, NIAH tests, multi-needle scenarios |
| [Health](docs/HEALTH.md) | Health checks, Kubernetes probes |
| [Benchmarks](docs/BENCHMARKS.md) | Performance measurements |
| [Guides](docs/GUIDES.md) | Usage patterns and best practices |
| [Roadmap](docs/ROADMAP.md) | Feature timeline and status |

## Research Foundation

Built on cutting-edge memory research:
- **MemGPT**: OS-inspired virtual memory paging
- **Mem0/Mem0g**: Graph-based memory networks
- **H-MEM**: Hierarchical memory with index routing
- **Cognitive Psychology**: Atkinson-Shiffrin, Baddeley, Tulving models

## License

MIT License - see [LICENSE](LICENSE) for details.

---

**Built by [iyulab](https://github.com/iyulab)**
