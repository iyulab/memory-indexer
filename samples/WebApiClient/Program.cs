using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Sdk.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Memory Indexer services
builder.Services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.InMemory;
    options.Embedding.Provider = EmbeddingProvider.Local;
    options.Embedding.Model = "all-MiniLM-L6-v2";
    options.Embedding.Dimensions = 384;
});

// Add MCP Server with HTTP transport
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "memory-api", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithMemoryTools();

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Memory Indexer API", Version = "v1" });
});

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map MCP endpoint for LLM clients
app.MapMcp("/mcp");

// REST API endpoints for direct access
var api = app.MapGroup("/api/memories");

api.MapPost("/", async (
    StoreMemoryRequest request,
    IMemoryStore store,
    IEmbeddingService embedding) =>
{
    var vector = await embedding.GenerateEmbeddingAsync(request.Content);
    var memory = new MemoryUnit
    {
        Id = Guid.NewGuid(),
        Content = request.Content,
        UserId = request.UserId ?? "default",
        SessionId = request.SessionId ?? "default",
        Embedding = vector,
        Type = Enum.TryParse<MemoryType>(request.Type, true, out var type) ? type : MemoryType.Episodic,
        ImportanceScore = request.Importance ?? 0.5f
    };

    await store.StoreAsync(memory);
    return Results.Created($"/api/memories/{memory.Id}", new { id = memory.Id, content = memory.Content });
})
.WithName("StoreMemory")
.WithOpenApi();

api.MapGet("/search", async (
    string query,
    int? limit,
    string? userId,
    IMemoryStore store,
    IEmbeddingService embedding) =>
{
    var queryVector = await embedding.GenerateEmbeddingAsync(query);
    var results = await store.SearchAsync(
        queryVector,
        new MemorySearchOptions
        {
            Limit = limit ?? 5,
            UserId = userId
        });

    return Results.Ok(results.Select(r => new
    {
        id = r.Memory.Id,
        content = r.Memory.Content,
        score = r.Score,
        type = r.Memory.Type.ToString()
    }));
})
.WithName("SearchMemories")
.WithOpenApi();

api.MapGet("/{id:guid}", async (Guid id, IMemoryStore store) =>
{
    var memory = await store.GetAsync(id);
    return memory is not null
        ? Results.Ok(new { memory.Id, memory.Content, memory.Type, memory.ImportanceScore })
        : Results.NotFound();
})
.WithName("GetMemory")
.WithOpenApi();

api.MapDelete("/{id:guid}", async (Guid id, IMemoryStore store) =>
{
    var deleted = await store.DeleteAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
})
.WithName("DeleteMemory")
.WithOpenApi();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Info endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "Memory Indexer Web API Sample",
    version = "1.0.0",
    endpoints = new
    {
        mcp = "/mcp (MCP over HTTP/SSE)",
        api = "/api/memories (REST API)",
        swagger = "/swagger (OpenAPI docs)",
        health = "/health"
    }
}));

Console.WriteLine("Memory Indexer Web API Sample");
Console.WriteLine("  REST API: http://localhost:5000/api/memories");
Console.WriteLine("  MCP Endpoint: http://localhost:5000/mcp");
Console.WriteLine("  Swagger: http://localhost:5000/swagger");

app.Run();

// Request DTOs
record StoreMemoryRequest(
    string Content,
    string? UserId = null,
    string? SessionId = null,
    string? Type = null,
    float? Importance = null);
