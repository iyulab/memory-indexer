using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Parse command-line arguments for transport mode
var transportMode = args.Contains("--http") || args.Contains("--sse") ? "http" : "stdio";
var httpPort = 3001;

// Check for custom port
var portIndex = Array.FindIndex(args, a => a == "--port");
if (portIndex >= 0 && portIndex + 1 < args.Length)
{
    int.TryParse(args[portIndex + 1], out httpPort);
}

if (transportMode == "http")
{
    // HTTP/SSE Transport Mode (ASP.NET Core)
    await RunHttpServer(args, httpPort);
}
else
{
    // Stdio Transport Mode (Default)
    await RunStdioServer(args);
}

/// <summary>
/// Runs the MCP server with stdio transport (default mode for Claude Desktop).
/// </summary>
static async Task RunStdioServer(string[] args)
{
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
            options.ServerInstructions = GetServerInstructions();
        })
        .WithStdioServerTransport()
        .WithMemoryTools();

    // Build and run
    var app = builder.Build();
    await app.RunAsync();
}

/// <summary>
/// Runs the MCP server with HTTP/SSE transport for web-based clients.
/// </summary>
static async Task RunHttpServer(string[] args, int port)
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(
        builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

    // Add Memory Indexer services
    builder.Services.AddMemoryIndexer();

    // Configure MCP Server with HTTP transport
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "memory-indexer",
                Version = "0.1.0"
            };
            options.ServerInstructions = GetServerInstructions();
        })
        .WithHttpTransport()
        .WithMemoryTools();

    // Configure Kestrel to use the specified port
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(port);
    });

    var app = builder.Build();

    // Map MCP endpoints
    app.MapMcp("/mcp");

    // Add a simple health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", server = "memory-indexer", version = "0.1.0" }));

    // Add info endpoint
    app.MapGet("/", () => Results.Ok(new
    {
        name = "Memory Indexer MCP Server",
        version = "0.1.0",
        transport = "HTTP/SSE",
        endpoints = new
        {
            mcp = "/mcp",
            health = "/health"
        },
        instructions = "Connect to /mcp endpoint using MCP client with HTTP transport"
    }));

    Console.WriteLine($"Memory Indexer MCP Server (HTTP/SSE) starting on http://localhost:{port}");
    Console.WriteLine($"  MCP Endpoint: http://localhost:{port}/mcp");
    Console.WriteLine($"  Health Check: http://localhost:{port}/health");
    Console.WriteLine();
    Console.WriteLine("Press Ctrl+C to stop the server.");

    await app.RunAsync();
}

/// <summary>
/// Gets the server instructions for MCP clients.
/// </summary>
static string GetServerInstructions() => """
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
