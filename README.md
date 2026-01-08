# Memory Indexer

A cognitive memory system for LLMs implementing human-inspired 4-tier memory architecture.

[![CI](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MemoryIndexer?logo=nuget)](https://www.nuget.org/packages/MemoryIndexer)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

## Philosophy

> *"The goal of memory is not to transmit the most accurate information over time, but to guide and optimize intelligent decision-making by only preserving valuable information."*
> — Richards & Frankland (2017)

LLMs face a fundamental constraint: **finite context windows**. Memory Indexer solves this by implementing forgetting as a feature, not a bug—inspired by how human memory actually works.

## Role & Scope

| What It Is | What It Isn't |
|------------|---------------|
| General-purpose memory primitives | A chatbot framework |
| Cognitive science-based architecture | A vector database |
| MCP server for any LLM client | Tied to specific use cases |
| Domain-agnostic building blocks | An opinionated application |

## Core Architecture

```
4-Tier Cognitive Memory (Atkinson-Shiffrin + Tulving):

┌────────────────────────────────────────────────────┐
│  Buffer (T0) - Sensory Store                       │
│  TTL: 60s idle │ 500 tokens │ 3 turns              │
├────────────────────────────────────────────────────┤
│  Short (T1) - Working Memory (Baddeley's 7±2)      │
│  Capacity: 9 items, auto-promote when exceeded     │
├────────────────────────────────────────────────────┤
│  Long (T2) - Episodic Memory                       │
│  Session-level events and experiences              │
├────────────────────────────────────────────────────┤
│  Archive (T3) - Semantic Memory                    │
│  Promotion: Confidence ≥ 0.8 AND Confirms ≥ 3      │
└────────────────────────────────────────────────────┘
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
services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Embedding.Provider = EmbeddingProvider.Ollama;
    options.Embedding.Model = "bge-m3";
});

// Store
await memoryService.StoreAsync("user123", "User prefers dark mode", importance: 0.8f);

// Recall
var results = await memoryService.RecallAsync("user123", "UI preferences", limit: 5);
```

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/ARCHITECTURE.md) | System design and 4-tier model |
| [Vision](docs/VISION.md) | Research basis and design philosophy |
| [Benchmarks](docs/BENCHMARKS.md) | Performance measurements |
| [Guides](docs/GUIDES.md) | Common patterns and best practices |
| [Changelog](CHANGELOG.md) | Version history |

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
