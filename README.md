# Memory Indexer

**Cognitive Memory System for LLMs** — An MCP server implementing human-inspired memory architecture with 4-Tier Virtual Context Management.

[![CI](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MemoryIndexer?logo=nuget)](https://www.nuget.org/packages/MemoryIndexer)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

## Why Memory Indexer?

LLMs face a fundamental constraint: **finite context windows**. Memory Indexer solves this by implementing a cognitive architecture inspired by human memory systems—where forgetting is not a bug, but a feature.

> *"The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."*
> — Richards & Frankland (2017), "The Persistence and Transience of Memory"

### Core Value

| Feature | Description |
|---------|-------------|
| **Zero Configuration** | Works out-of-the-box with sensible defaults |
| **4-Tier Cognitive Architecture** | Buffer → Short-Term → Long-Term → Archive (cognitive science-inspired) |
| **Intelligent Forgetting** | Ebbinghaus curve-based decay with importance weighting |
| **Domain Agnostic** | General-purpose memory primitives, not tied to specific use cases |
| **Research-Based** | Built on MemGPT, Mem0, H-MEM, and cognitive psychology research |

## Quick Start

### As MCP Server (with Claude Desktop)

```bash
# Install
dotnet tool install -g MemoryIndexer.Mcp
```

Configure Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "memory-indexer": {
      "command": "memory-indexer-mcp",
      "args": []
    }
  }
}
```

### As SDK (in your .NET application)

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

// Store
await memoryService.StoreAsync(
    userId: "user123",
    content: "User prefers dark mode",
    importance: 0.8f
);

// Recall
var results = await memoryService.RecallAsync(
    userId: "user123",
    query: "UI preferences",
    limit: 5
);
```

## 4-Tier Cognitive Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Buffer (T0): Raw conversation staging                  │
│  TTL: 60s idle OR 500 tokens OR 3 turns                 │
│  (Atkinson-Shiffrin sensory memory)                     │
├─────────────────────────────────────────────────────────┤
│  Short-Term (T1): Active context, 4-7 chunks            │
│  TTL: 10min OR 2K tokens OR topic change                │
│  (Baddeley's working memory model)                      │
├─────────────────────────────────────────────────────────┤
│  Long-Term (T2): Session experiences, vector search     │
│  Storage: SQLite-vec (default) or Qdrant                │
│  (Tulving's episodic memory - event-based)              │
├─────────────────────────────────────────────────────────┤
│  Archive (T3): Long-term knowledge dictionary           │
│  Promotion: Confidence ≥ 0.8 AND Confirms ≥ 3           │
│  (Tulving's semantic memory - fact-based)               │
└─────────────────────────────────────────────────────────┘
```

## Key Features

- **Hybrid Search**: Semantic (embeddings) + Keyword (BM25) + Metadata boosting
- **Smart Deduplication**: Content-aware duplicate detection (80% similarity threshold)
- **Query Intent Classification**: Factual/Contextual/Temporal/Relational routing
- **Graph Memory Network**: Entity extraction, community detection, PageRank importance
- **Self-Directed Management**: MemGPT-inspired autonomous consolidation
- **Production Observability**: OpenTelemetry integration, health checks, metrics

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/ARCHITECTURE.md) | System design and 4-tier VCM details |
| [Vision](docs/VISION.md) | Research basis and design principles |
| [Tier × Type Matrix](docs/TIER_TYPE_MATRIX.md) | Understanding memory tiers vs types |
| [Usage Guides](docs/GUIDES.md) | Common patterns and best practices |
| [Integrations](docs/INTEGRATIONS.md) | Semantic Kernel, LangChain, AutoGen |

## Configuration

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "memory.db"
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "bge-m3",
      "Dimensions": 1024
    },
    "VCM": {
      "ShortTermMemory": { "Capacity": 7, "DefaultTtl": "00:10:00" },
      "Buffer": { "MaxIdleSeconds": 60, "TokenThreshold": 500 }
    }
  }
}
```

## Research References

Memory Indexer builds on cutting-edge research:
- **MemGPT**: OS-inspired virtual memory paging
- **Mem0/Mem0g**: Graph-based memory networks
- **H-MEM**: Hierarchical memory with index routing
- **AFM**: Adaptive fidelity memory
- **SLEEP Paradigm**: Memory consolidation during rest cycles

## License

MIT License - see [LICENSE](LICENSE) file for details.

---

**Built with ❤️ by [iyulab](https://github.com/iyulab)**
