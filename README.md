# Memory Indexer

A .NET MCP server for LLM long-term memory management.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

## Overview

Memory Indexer is a [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that provides semantic memory storage and retrieval for LLM applications. It enables AI assistants to maintain persistent memory across conversations.

## Features

- **Semantic Search** - Vector-based similarity search for relevant context retrieval
- **MCP Integration** - Standard MCP tools for Claude Desktop and other MCP clients
- **Flexible Storage** - Pluggable storage backends (InMemory, SQLite, Qdrant)
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
- [Roadmap](docs/ROADMAP.md)

## License

MIT
