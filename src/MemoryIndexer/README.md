# MemoryIndexer

**Core abstractions and minimal implementations for LLM memory management.**

This package provides the foundational interfaces, models, and lightweight implementations for the Memory Indexer system. It has minimal external dependencies, making it ideal for:

- Custom implementations of storage/embedding providers
- Unit testing with InMemory implementations
- Building your own memory management solution

## Quick Start

```csharp
services.AddMemoryIndexerCore(options =>
{
    options.Search.DefaultLimit = 10;
    options.VCM.WorkingMemoryCapacity = 7;
});
```

## What's Included

### Interfaces
- `IMemoryStore` - Memory storage operations
- `IEmbeddingService` - Embedding generation
- `IScoringService` - Memory relevance scoring
- `ISessionStore` - Session management
- `IVirtualContextManager` - Context window management

### Models
- `MemoryUnit` - Core memory entity
- `Session` - Conversation session
- `EntityTriple` - Knowledge graph entities

### Implementations (Minimal Dependencies)
- `InMemoryMemoryStore` - In-memory storage for testing
- `MockEmbeddingService` - Deterministic embeddings for testing
- `DefaultScoringService` - Hybrid scoring algorithm

## For Full Features

Use `MemoryIndexer.Sdk` for production features:
- InMemory/SQLite storage (extensible via IMemoryStore)
- LMSupply embeddings (ONNX-based)
- MCP protocol tools
- OpenTelemetry observability

```bash
dotnet add package MemoryIndexer.Sdk
```

## License

MIT
