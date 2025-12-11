# Memory Indexer SDK

Long-term memory management for LLM applications via MCP (Model Context Protocol).

## Features

- **Semantic Search**: Vector-based similarity search with hybrid BM25 + embedding retrieval
- **Multiple Storage Backends**: InMemory, SQLite-vec, and Qdrant
- **Embedding Providers**: Local (ONNX), Ollama, OpenAI, Azure OpenAI
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

// Add Memory Indexer with default settings
builder.Services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Embedding.Provider = EmbeddingProvider.Local;
});

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
      "Type": "SqliteVec",
      "ConnectionString": "memories.db"
    },
    "Embedding": {
      "Provider": "Local",
      "Dimensions": 1024,
      "CacheEnabled": true
    },
    "Search": {
      "DefaultLimit": 10,
      "MinimumScore": 0.5
    }
  }
}
```

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
