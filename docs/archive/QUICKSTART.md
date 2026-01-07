# Quick Start Guide

Get Memory Indexer running in **5 minutes** with this step-by-step guide.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- [Claude Desktop](https://claude.ai/download) (for MCP integration)

## Option 1: MCP Server (Recommended for Claude Desktop)

### Step 1: Install from Source (2 min)

```bash
# Clone repository
git clone https://github.com/iyulab/memory-indexer.git
cd memory-indexer

# Build project
dotnet build
```

### Step 2: Configure Claude Desktop (1 min)

Add to your Claude Desktop config file:

**Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
**macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
**Linux**: `~/.config/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "memory-indexer": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/path/to/memory-indexer/tools/McpServer"
      ]
    }
  }
}
```

> **Note**: Replace `C:/path/to/memory-indexer` with your actual path. Use forward slashes (`/`) even on Windows.

### Step 3: Restart Claude Desktop (10 sec)

Restart Claude Desktop to load the MCP server.

### Step 4: Verify Installation (30 sec)

In Claude Desktop, check for MCP tools:

```
Available tools:
- memory_store
- memory_recall
- memory_get
- memory_list
- memory_update
- memory_delete
```

### Step 5: Store Your First Memory (1 min)

Try this in Claude Desktop:

```
Please store this memory: "I prefer TypeScript over JavaScript for large projects."
```

Claude will use the `memory_store` tool to save this preference.

### Step 6: Recall Memory (30 sec)

Ask Claude to recall:

```
What are my programming language preferences?
```

Claude will use `memory_recall` to find relevant memories.

✅ **Done!** You now have a working memory system for Claude.

---

## Option 2: SDK Integration (For Your .NET Applications)

### Step 1: Install Package (30 sec)

```bash
dotnet add package MemoryIndexer.Sdk
```

### Step 2: Configure Services (2 min)

```csharp
using MemoryIndexer;
using MemoryIndexer.Sdk.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Memory Indexer
builder.Services.AddMemoryIndexer(options =>
{
    // Storage: SQLite with vector support (no external dependencies)
    options.Storage.Type = StorageType.SqliteVec;
    options.Storage.ConnectionString = "Data Source=memory.db";

    // Embedding: Use local ONNX model (offline-capable)
    options.Embedding.Provider = EmbeddingProvider.Local;
    options.Embedding.Model = "bge-small-en-v1.5";
    options.Embedding.Dimensions = 384;
});

var app = builder.Build();
```

### Step 3: Use Memory Service (2 min)

```csharp
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

public class ChatService
{
    private readonly IMemoryPrimitives _memory;

    public ChatService(IMemoryPrimitives memory)
    {
        _memory = memory;
    }

    public async Task StoreConversation(string userId, string message)
    {
        await _memory.EncodeAsync(new MemoryUnit
        {
            UserId = userId,
            Content = message,
            Type = MemoryType.Episodic,
            Metadata = new Dictionary<string, object>
            {
                ["timestamp"] = DateTime.UtcNow
            }
        });
    }

    public async Task<List<MemoryUnit>> RecallContext(string userId, string query)
    {
        var results = await _memory.RetrieveAsync(userId, query, limit: 5);
        return results.ToList();
    }
}
```

✅ **Done!** Your app now has cognitive memory capabilities.

---

## What's Next?

### Explore Advanced Features

- **4-Tier Memory Architecture**: Understand Recently → Working → Session → User tiers ([Architecture](ARCHITECTURE.md))
- **Smart Retrieval**: Learn about hybrid search and query intent classification ([Patterns](PATTERNS.md))
- **Production Deployment**: Deploy to Kubernetes ([Deployment Guide](../deploy/kubernetes/README.md))

### Common Use Cases

See [Patterns Guide](PATTERNS.md) for:
- Conversation history with automatic summarization
- User preference learning and recall
- Long-term fact accumulation
- Entity relationship tracking

### Integration Examples

Check [Integration Guide](INTEGRATIONS.md) for:
- LangChain integration
- Semantic Kernel integration
- AutoGen multi-agent systems
- Custom LLM frameworks

### Configuration Tuning

Optimize for your workload ([Best Practices](BEST_PRACTICES.md)):
- Memory tier capacity tuning
- Promotion threshold optimization
- Embedding provider selection
- Vector database configuration

---

## Troubleshooting

### MCP Server Not Showing in Claude Desktop

**Check 1**: Verify config file path is correct
**Check 2**: Ensure forward slashes in path (even on Windows)
**Check 3**: Check Claude Desktop logs: `%APPDATA%\Claude\logs\`
**Check 4**: Run server manually to check for errors:

```bash
cd memory-indexer
dotnet run --project tools/McpServer
```

### "Could not find sqlite-vec extension"

**Solution**: The extension is bundled with the package. Ensure you're using `MemoryIndexer.Sdk` package.

### "Failed to load ONNX model"

**Solution 1**: Use Ollama provider instead:

```csharp
options.Embedding.Provider = EmbeddingProvider.Ollama;
options.Embedding.Model = "bge-m3";
```

**Solution 2**: Install [Ollama](https://ollama.ai/) and pull the model:

```bash
ollama pull bge-m3
```

### High Memory Usage

**Solution**: Enable lazy embedding loading:

```csharp
options.VCM.WorkingMemory.LazyEmbeddingLoading = true;
```

See [Memory Optimization Guide](MEMORY_OPTIMIZATION.md) for more.

---

## Resources

- **Documentation**: [docs/](.)
- **Samples**: [samples/](../samples/)
- **GitHub Issues**: [Report a bug](https://github.com/iyulab/memory-indexer/issues)
- **Discussions**: [Ask questions](https://github.com/iyulab/memory-indexer/discussions)

---

**Time to first memory**: ⏱️ **5 minutes**
**Zero external dependencies**: ✅ SQLite + Local embeddings
**Production ready**: 🚀 Kubernetes manifests included
