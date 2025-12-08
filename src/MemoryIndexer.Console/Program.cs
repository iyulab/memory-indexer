using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Build and configure the host
var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr (stdout is reserved for MCP communication)
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Set minimum log level based on environment
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

// Add Memory Indexer services
builder.Services.AddMemoryIndexer();

// Configure MCP Server with stdio transport
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "memory-indexer",
            Version = "0.1.0"
        };
        options.ServerInstructions = """
            Memory Indexer MCP Server - Long-term memory management for LLM conversations.

            Available tools:
            - store_memory: Store content in long-term memory with semantic indexing
            - recall_memory: Search memories using semantic similarity
            - get_all_memories: Retrieve all stored memories with filtering
            - update_memory: Update existing memory content or importance
            - delete_memory: Delete a memory by ID
            - get_memory: Get detailed information about a specific memory

            Memory types:
            - episodic: Specific events and experiences with temporal context
            - semantic: General facts and knowledge
            - procedural: How-to information and workflows
            - fact: Specific verifiable facts

            Best practices:
            - Use importance scores (0.0-1.0) to prioritize critical information
            - Group related memories using session IDs
            - Use semantic search (recall_memory) for relevant context retrieval
            """;
    })
    .WithStdioServerTransport()
    .WithMemoryTools();

// Build and run
var app = builder.Build();

await app.RunAsync();
