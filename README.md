# Memory Indexer

A .NET MCP server for LLM long-term memory management.

[![CI](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/memory-indexer/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MemoryIndexer?logo=nuget)](https://www.nuget.org/packages/MemoryIndexer)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-282%20passing-brightgreen)]()

## Overview

Memory Indexer is a [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that provides semantic memory storage and retrieval for LLM applications. It enables AI assistants to maintain persistent memory across conversations.

### Key Benefits

| Scenario | Without Memory | With Memory | Improvement |
|----------|----------------|-------------|-------------|
| **Short-term recall** | 50% | 83% | +33% |
| **Cross-session recall** | 0% | 79% | **+79%** |
| **Topic switching** | 0% | 93% | **+93%** |

See the full [Effectiveness Report](docs/EFFECTIVENESS_REPORT.md) for detailed analysis.

## Features

- **Semantic Search** - Vector-based similarity search with hybrid retrieval (dense + sparse)
- **Full-Text Search** - FTS5-based BM25 ranking with trigram tokenizer (CJK/multilingual)
- **Hybrid Search** - RRF (Reciprocal Rank Fusion) combining vector and text search
- **Query Expansion** - Automatic synonym and context expansion for better recall
- **MCP Integration** - Standard MCP tools for Claude Desktop and other MCP clients
- **Dual Storage** - SQLite-vec (local) and Qdrant (production) backends
- **Local Embeddings** - Built-in support for local embedding models (all-MiniLM-L6-v2)
- **SDK Package** - Embeddable NuGet package for .NET applications

## Quick Start

### Build & Run

```bash
dotnet build
dotnet run --project src/MemoryIndexer.Console
```

### Claude Desktop Configuration

Add to `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "memory-indexer": {
      "command": "path/to/memory-indexer.exe"
    }
  }
}
```

## Storage Backend Selection

Memory Indexer supports multiple storage backends optimized for different use cases:

### Storage Options

| Storage | Best For | Features | Dependencies |
|---------|----------|----------|--------------|
| **SQLite-vec** | Local dev, SDK embedded, MCP stdio | Single file, FTS5, hybrid search | Zero external |
| **Qdrant** | Production, high-scale | HNSW index, distributed, BM42 | Qdrant server |
| **InMemory** | Testing only | Fast, ephemeral | None |

### Usage Scenarios

| Scenario | Recommended Storage | Configuration |
|----------|---------------------|---------------|
| **SDK embedded in app** | SQLite-vec | `appsettings.sdk-embedded.json` |
| **MCP server (stdio)** | SQLite-vec | `appsettings.mcp-server.json` |
| **Production deployment** | Qdrant | `appsettings.qdrant-production.json` |
| **Unit testing** | InMemory | `Storage.Type = "InMemory"` |

### SQLite-vec Configuration

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "Sqlite": {
        "DatabasePath": "memories.db",
        "UseWalMode": true,
        "FtsTokenizer": "trigram",
        "CacheSizeKb": 2000,
        "EnableVectorSearch": true,
        "EnableFullTextSearch": true,
        "BusyTimeoutMs": 5000
      }
    },
    "Embedding": {
      "Provider": "Local",
      "Model": "all-MiniLM-L6-v2",
      "Dimensions": 384
    }
  }
}
```

### Qdrant Configuration

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "Qdrant",
      "Qdrant": {
        "Host": "localhost",
        "Port": 6334,
        "ApiKey": null,
        "CollectionName": "memories"
      }
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "nomic-embed-text",
      "Dimensions": 768,
      "Endpoint": "http://localhost:11434"
    }
  }
}
```

### FTS5 Tokenizer Options

| Tokenizer | Use Case | Description |
|-----------|----------|-------------|
| `trigram` | CJK, multilingual | Best for Korean, Chinese, Japanese |
| `unicode61` | Unicode standard | General multilingual support |
| `porter` | English only | English stemming |

## MCP Tools

| Tool | Description |
|------|-------------|
| `memory_store` | Store content with semantic indexing |
| `memory_recall` | Search memories by semantic similarity |
| `memory_get` | Retrieve a specific memory by ID |
| `memory_list` | List memories with optional filters |
| `memory_update` | Update memory content or metadata |
| `memory_delete` | Delete a memory |

## SDK Usage

```csharp
// Add Memory Indexer to your application
services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Storage.Sqlite = new SqliteOptions
    {
        DatabasePath = "app_memories.db",
        UseWalMode = true
    };
    options.Embedding.Provider = EmbeddingProvider.Local;
    options.Embedding.Model = "all-MiniLM-L6-v2";
    options.Embedding.Dimensions = 384;
});

// Use the memory service
var memoryService = serviceProvider.GetRequiredService<MemoryService>();
await memoryService.StoreAsync(new MemoryUnit
{
    UserId = "user-123",
    Content = "User prefers dark mode",
    Type = MemoryType.Semantic
});
```

## Search Configuration

### Hybrid Search Weights

```json
{
  "Search": {
    "DenseWeight": 0.6,
    "SparseWeight": 0.4,
    "RrfK": 60,
    "MmrLambda": 0.7
  }
}
```

- `DenseWeight`: Weight for vector similarity (0.0-1.0)
- `SparseWeight`: Weight for BM25 text match (0.0-1.0)
- `RrfK`: RRF constant (typically 60)
- `MmrLambda`: MMR diversity parameter (higher = more relevance, lower = more diversity)

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Effectiveness Report](docs/EFFECTIVENESS_REPORT.md)
- [Roadmap](docs/ROADMAP.md)

## Project Status

**v0.1.0 Released**: Full-featured memory management SDK.

### Features
- 6 core MCP tools + advanced search tools
- **SQLite-vec storage** with FTS5 hybrid search
- **Qdrant integration** for production scale
- Local embedding support (all-MiniLM-L6-v2, 384 dimensions)
- Hybrid search with BM25 + dense vectors + RRF fusion
- Query expansion for improved recall
- **Knowledge Graph** with entity extraction
- **Self-editing memory** management
- **PII detection** and prompt injection defense
- **Multi-tenant isolation** with CTE-based pre-filtering
- **OpenTelemetry** observability (tracing & metrics)
- **LoCoMo benchmark** evaluation suite

### Test Coverage
- 282 tests (Core, Storage, Intelligence, Integration)
- Evaluation metrics: Recall, Precision, MRR, NDCG

## Installation

```bash
# NuGet Package
dotnet add package MemoryIndexer
```

## License

MIT
