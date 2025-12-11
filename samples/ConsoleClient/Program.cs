using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Console.WriteLine("=== Memory Indexer Console Client Sample ===");
Console.WriteLine();

// Build the host with Memory Indexer services
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMemoryIndexer(options =>
{
    // Use InMemory storage for this sample
    options.Storage.Type = StorageType.InMemory;

    // Use local embedding model
    options.Embedding.Provider = EmbeddingProvider.Local;
    options.Embedding.Model = "all-MiniLM-L6-v2";
    options.Embedding.Dimensions = 384;
});

var host = builder.Build();

// Get services
var memoryStore = host.Services.GetRequiredService<IMemoryStore>();
var embeddingService = host.Services.GetRequiredService<IEmbeddingService>();

Console.WriteLine("Services initialized successfully!");
Console.WriteLine($"Embedding dimensions: {embeddingService.Dimensions}");
Console.WriteLine();

// Sample memories to store
var memories = new[]
{
    "Python is a versatile programming language used for web development, data science, and machine learning.",
    "JavaScript is essential for front-end web development and can also be used on the server with Node.js.",
    "Docker containers allow applications to run consistently across different computing environments.",
    "Machine learning models learn patterns from data to make predictions without explicit programming.",
    "REST APIs use HTTP methods like GET, POST, PUT, and DELETE to communicate between services."
};

// Store memories
Console.WriteLine("Storing memories...");
var storedIds = new List<Guid>();

foreach (var content in memories)
{
    var embedding = await embeddingService.GenerateEmbeddingAsync(content);
    var memory = new MemoryUnit
    {
        Id = Guid.NewGuid(),
        Content = content,
        UserId = "sample-user",
        SessionId = "sample-session",
        Embedding = embedding,
        Type = MemoryType.Semantic,
        ImportanceScore = 0.8f
    };

    await memoryStore.StoreAsync(memory);
    storedIds.Add(memory.Id);
    Console.WriteLine($"  - Stored: {content[..Math.Min(50, content.Length)]}...");
}

Console.WriteLine($"\nStored {storedIds.Count} memories successfully!");
Console.WriteLine();

// Search memories
var queries = new[]
{
    "How do I build web applications?",
    "What is machine learning?",
    "How do containers work?"
};

foreach (var query in queries)
{
    Console.WriteLine($"Query: \"{query}\"");
    Console.WriteLine("-".PadRight(60, '-'));

    var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query);
    var results = await memoryStore.SearchAsync(
        queryEmbedding,
        new MemorySearchOptions { Limit = 3, UserId = "sample-user" });

    foreach (var result in results)
    {
        Console.WriteLine($"  [{result.Score:F4}] {result.Memory.Content[..Math.Min(60, result.Memory.Content.Length)]}...");
    }

    Console.WriteLine();
}

// Cleanup
Console.WriteLine("Sample completed successfully!");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
