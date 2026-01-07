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

### Core Value Propositions

- **Zero Configuration**: Works out-of-the-box with sensible defaults
- **4-Tier Cognitive Architecture**: Sensory → Working → Episodic → Semantic (cognitive science-inspired memory tiers)
- **Intelligent Forgetting**: Ebbinghaus curve-based decay with importance weighting
- **Production Ready**: 848 tests, comprehensive observability, deployment guides
- **Research-Based**: Built on MemGPT, Mem0, H-MEM, and cognitive psychology research

## Quick Start

### As MCP Server (with Claude Desktop)

1. **Install the MCP server:**
```bash
dotnet tool install -g MemoryIndexer.Mcp
```

2. **Configure Claude Desktop** (`%APPDATA%\Claude\claude_desktop_config.json`):
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

3. **Restart Claude Desktop** and start using memory tools in conversations.

### As SDK (in your .NET application)

1. **Install the package:**
```bash
dotnet add package MemoryIndexer.Sdk
```

2. **Configure services:**
```csharp
services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;  // Zero-config default
    options.Embedding.Provider = EmbeddingProvider.Ollama;
    options.Embedding.Model = "bge-m3";
});
```

3. **Use the memory service:**
```csharp
// Store a memory
await memoryService.StoreAsync(
    userId: "user123",
    content: "User prefers dark mode",
    type: MemoryType.Fact,
    importance: 0.8f
);

// Recall relevant memories
var results = await memoryService.RecallAsync(
    userId: "user123",
    query: "UI preferences",
    limit: 5
);
```

## 4-Tier Cognitive Architecture

```
┌─────────────────────────────────────────────────────┐
│  Sensory (T0): Raw conversation staging            │
│  TTL: 60s idle OR 500 tokens OR 3 turns            │
│  (Atkinson-Shiffrin sensory memory)                │
├─────────────────────────────────────────────────────┤
│  Working (T1): Active context, 4-7 chunks          │
│  TTL: 10min OR 2K tokens OR topic change           │
│  (Baddeley's working memory model)                 │
├─────────────────────────────────────────────────────┤
│  Episodic (T2): Session experiences, vector search │
│  Storage: SQLite-vec (default) or Qdrant           │
│  (Tulving's episodic memory - event-based)         │
├─────────────────────────────────────────────────────┤
│  Semantic (T3): Long-term knowledge dictionary      │
│  Promotion: Confidence ≥ 0.8 AND Confirms ≥ 3      │
│  (Tulving's semantic memory - fact-based)          │
└─────────────────────────────────────────────────────┘
```

**Multi-Signal Promotion:**
- **Lower tiers**: OR logic (time OR tokens OR turns) — aggressive cleanup
- **Upper tier**: AND logic (confidence AND frequency) — conservative promotion

## Key Features

- **Hybrid Search**: Semantic (embeddings) + Keyword (BM25) + Metadata boosting
- **Smart Deduplication**: Content-aware duplicate detection with 80% similarity threshold
- **Query Intent Classification**: Factual/Contextual/Temporal/Relational routing
- **Graph Memory Network**: Entity extraction, community detection, PageRank importance
- **Self-Directed Management**: MemGPT-inspired autonomous consolidation and reflection
- **Structured Metadata**: Type-safe JSON-serialized metadata with filtering
- **Time-Series Compression**: Automatic metadata compression (e.g., "1-20" instead of "1, 2, 3...")
- **Production Observability**: OpenTelemetry integration, health checks, metrics

## Documentation

- **[Quick Start Guide](docs/QUICKSTART.md)** — 5-minute setup for MCP and SDK
- **[Architecture](docs/ARCHITECTURE.md)** — System design and 4-tier VCM details
- **[Tier × Type Matrix](docs/TIER_TYPE_MATRIX.md)** — Understanding memory tiers vs types (orthogonal dimensions)
- **[Vision & Philosophy](docs/VISION.md)** — Research basis and design principles
- **[Usage Patterns](docs/GUIDES.md)** — Common patterns, best practices, anti-patterns
- **[Integrations](docs/INTEGRATIONS.md)** — Semantic Kernel, LangChain, AutoGen
- **[Migration Guide](docs/MIGRATION_GUIDE.md)** — Storage migration and upgrades
- **[Migration to v0.4](docs/MIGRATION_V0.4.md)** — Cognitive terminology migration guide
- **[Roadmap](docs/ROADMAP.md)** — Feature timeline and completed phases

## Configuration Example

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
      "WorkingMemory": { "Capacity": 7, "DefaultTtl": "00:10:00" },
      "SensoryBuffer": { "MaxIdleSeconds": 60, "TokenThreshold": 500 },
      "SemanticStore": { "MinConfirmationCount": 3, "MinConfidenceThreshold": 0.8 }
    }
  }
}
```

## Project Status

- **Version**: v0.3.0
- **Tests**: 848 passing (49 Core + 799 SDK)
- **Target**: .NET 10.0
- **License**: MIT

### Recent Updates (v0.3.0)

- **Phase 28**: Structured Metadata API with type-safe JSON serialization
- **Phase 29**: Time-Series Compression (Range/Statistical/Windowed strategies)
- **Phase 27**: SQLite Zero-Config Auto-Management
- **Phase 26**: LLM-powered Memory Conflict Resolution
- **Phase 25**: Semantic Knowledge Extraction from Q&A
- **Phase 20-23**: Smart Deduplication, Quality Control, Type Balancing

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

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
