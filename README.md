# Memory Indexer

A .NET MCP server for LLM long-term memory management.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-159%20passing-brightgreen)]()

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
- **Query Expansion** - Automatic synonym and context expansion for better recall
- **MCP Integration** - Standard MCP tools for Claude Desktop and other MCP clients
- **Flexible Storage** - Pluggable storage backends (InMemory, Qdrant)
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

## MCP Tools

| Tool | Description |
|------|-------------|
| `memory_store` | Store content with semantic indexing |
| `memory_recall` | Search memories by semantic similarity |
| `memory_get` | Retrieve a specific memory by ID |
| `memory_list` | List memories with optional filters |
| `memory_update` | Update memory content or metadata |
| `memory_delete` | Delete a memory |

## Configuration

```json
{
  "MemoryIndexer": {
    "Storage": { "Type": "InMemory" },
    "Embedding": { "Dimensions": 1024 },
    "Search": { "DefaultLimit": 10 }
  }
}
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Effectiveness Report](docs/EFFECTIVENESS_REPORT.md)
- [Roadmap](docs/ROADMAP.md)

## Project Status

**Phase 3 Complete**: Advanced intelligence features implemented.

- 6 core MCP tools + advanced search tools
- Local embedding support (all-MiniLM-L6-v2, 384 dimensions)
- Hybrid search with BM25 + dense vectors
- Query expansion for improved recall
- Qdrant integration for production scale
- 159 passing tests

## License

MIT
