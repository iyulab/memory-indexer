using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Sdk.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
    if (int.TryParse(args[portIndex + 1], out var parsedPort))
    {
        httpPort = parsedPort;
    }
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

    // Add Health Checks
    builder.Services.AddMemoryIndexerHealthChecks();

    // Add REST API Controllers
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Configure MCP Server with HTTP transport
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "memory-indexer",
                Version = "0.3.0"
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

    // Enable Swagger middleware
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Memory Indexer API v1");
        c.RoutePrefix = "swagger";
    });

    // Map MCP endpoints
    app.MapMcp("/mcp");

    // Map REST API controllers
    app.MapControllers();

    // Health Check Endpoints (Kubernetes-compatible)
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = HealthCheckResponseWriter.WriteResponse,
        Predicate = _ => true
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        ResponseWriter = HealthCheckResponseWriter.WriteResponse,
        Predicate = check => check.Tags.Contains("critical")
    });

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        ResponseWriter = HealthCheckResponseWriter.WriteResponse,
        Predicate = _ => true  // All checks for liveness
    });

    app.MapHealthChecks("/health/startup", new HealthCheckOptions
    {
        ResponseWriter = HealthCheckResponseWriter.WriteResponse,
        Predicate = check => check.Tags.Contains("infrastructure")
    });

    // Health check by tier
    app.MapGet("/health/tier/{tier}", async (string tier, HealthCheckService healthCheckService) =>
    {
        var result = await healthCheckService.CheckHealthAsync(
            check => check.Tags.Contains($"tier:{tier}"),
            CancellationToken.None);

        var status = result.Status == HealthStatus.Healthy ? 200 :
                     result.Status == HealthStatus.Degraded ? 200 : 503;

        return Results.Json(new
        {
            status = result.Status.ToString(),
            tier,
            checks = result.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data
            })
        }, statusCode: status);
    });

    // Add info endpoint
    app.MapGet("/", () => Results.Ok(new
    {
        name = "Memory Indexer MCP Server",
        version = "0.3.0",
        transport = "HTTP/SSE",
        endpoints = new
        {
            mcp = "/mcp",
            restApi = "/api/memory",
            swagger = "/swagger",
            health = "/health",
            healthReady = "/health/ready",
            healthLive = "/health/live",
            healthStartup = "/health/startup",
            healthByTier = "/health/tier/{tier}"
        },
        instructions = new
        {
            mcp = "Connect to /mcp endpoint using MCP client with HTTP transport",
            restApi = "Use /api/memory endpoints for REST API access (see /swagger for documentation)",
            swagger = "Visit /swagger for interactive API documentation"
        }
    }));

    Console.WriteLine($"Memory Indexer MCP Server (HTTP/SSE) starting on http://localhost:{port}");
    Console.WriteLine($"  MCP Endpoint: http://localhost:{port}/mcp");
    Console.WriteLine($"  REST API: http://localhost:{port}/api/memory");
    Console.WriteLine($"  Swagger UI: http://localhost:{port}/swagger");
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
