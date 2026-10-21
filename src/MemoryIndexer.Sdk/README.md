# Memory Indexer SDK

Long-term memory management for LLM applications via MCP (Model Context Protocol).

## Features

- **Semantic Search**: Vector-based similarity search with hybrid BM25 + embedding retrieval
- **Storage Backends**: InMemory, SQLite-vec (extensible via IMemoryStore)
- **Embedding Providers**: Inject your own via IEmbeddingService (OpenAI, Azure, local ONNX, etc.)
- **Resource limits**: per-user memory count and size limits checked on store (`MemoryIndexerOptions.ResourceLimits`);
  usage is keyed by the ambient tenant (`ITenantContextAccessor`) when a host sets one — there is no tenant isolation of
  stored memories
- **Security tools (opt-in)**: PII detection and prompt-injection checks as MCP tools, registered by
  `WithAllMemoryIndexerTools()` — nothing on the store or recall path calls them
- **Observability**: Built-in OpenTelemetry tracing and metrics
- **Evaluation (SDK components)**: LoCoMo and retrieval evaluators you resolve and run yourself
- **MCP Integration**: Ready-to-use MCP tools for Claude and other LLM clients

## Quick Start

```csharp
using MemoryIndexer.Interfaces;       // IEmbeddingService
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Sdk.Observability; // AddMemoryIndexerOtlpObservability
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Register your embedding service BEFORE AddMemoryIndexer()
builder.Services.AddSingleton<IEmbeddingService>(myEmbeddingService);

// Add Memory Indexer with SQLite-vec storage (sized from your service's Dimensions)
builder.Services.AddMemoryIndexer().WithSqliteVec();

// Optional: Add OpenTelemetry observability
builder.Services.AddMemoryIndexerOtlpObservability("http://localhost:4317");

// Add MCP server
builder.Services.AddMcpServer()
    .WithMemoryTools();

var host = builder.Build();
await host.RunAsync();
```

## Configuration

```json
{
  "MemoryIndexer": {
    "Storage": {
      "ConnectionString": "memories.db"
    },
    "Embedding": {
      "CacheEnabled": true
    },
    "Search": {
      "DefaultLimit": 10,
      "MinimumScore": 0.5
    }
  }
}
```

> **Note**: Embedding service must be registered externally via DI before calling `AddMemoryIndexer()`.

## MCP Tools

- `WithMemoryTools()` registers the seven memory tools — `store_memory`, `recall_memory`, `get_memory`, `get_all_memories`,
  `update_memory`, `delete_memory`, `confirm_memory` — and is what the bundled `memory-indexer` server serves.
  `store_memory` and `recall_memory` are `IMemoryService.RememberAsync` and the SDK's recall (classification, small-talk
  drop, recency read from `occurredAt`).
- `WithAllMemoryIndexerTools()` adds every other tool class: knowledge graph and graph traversal, fact extraction and
  fact conflicts (`extract_facts` …), contradiction resolution, adaptive retrieval, context building, profile evolution,
  retention policy, backup and restore, resource usage, self-editing memory, reflection, and the security tools.

## Requirements

- .NET 10.0 or later
- For local embeddings: ONNX Runtime compatible system

## License

MIT License
