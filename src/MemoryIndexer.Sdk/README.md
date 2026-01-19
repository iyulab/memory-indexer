# Memory Indexer SDK

Long-term memory management for LLM applications via MCP (Model Context Protocol).

## Features

- **Semantic Search**: Vector-based similarity search with hybrid BM25 + embedding retrieval
- **Storage Backends**: InMemory, SQLite-vec (extensible via IMemoryStore)
- **Embedding Providers**: Inject your own via IEmbeddingService (OpenAI, Azure, local ONNX, etc.)
- **Multi-Tenant Support**: Complete tenant isolation with CTE-based pre-filtering
- **Security**: PII detection and prompt injection defense
- **Observability**: Built-in OpenTelemetry tracing and metrics
- **Evaluation**: LoCoMo benchmark evaluation for memory retrieval quality
- **MCP Integration**: Ready-to-use MCP tools for Claude and other LLM clients

## Quick Start

```csharp
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Register your embedding service BEFORE AddMemoryIndexer()
builder.Services.AddSingleton<IEmbeddingService>(myEmbeddingService);

// Add Memory Indexer with SQLite-vec storage
builder.Services.AddMemoryIndexer(options =>
{
    options.Embedding.Dimensions = 1536;  // Match your embedding model
}).WithSqliteVec();

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
      "Dimensions": 1536,
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

The SDK provides these MCP tools:

- `memory_store`: Store new memories with semantic embeddings
- `memory_recall`: Retrieve relevant memories using semantic search
- `memory_get`: Get a specific memory by ID
- `memory_list`: List memories with filtering
- `memory_update`: Update memory content or importance
- `memory_delete`: Delete memories (soft or hard delete)
- `memory_kg_extract`: Extract knowledge graph entities
- `memory_kg_query`: Query the knowledge graph
- `memory_context_optimize`: Optimize context window usage
- `memory_pii_detect`: Detect PII in content
- `memory_sanitize`: Sanitize content for security

## Requirements

- .NET 10.0 or later
- For local embeddings: ONNX Runtime compatible system

## License

MIT License
