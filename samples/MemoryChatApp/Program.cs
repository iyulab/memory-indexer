using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using MemoryIndexer.Sdk.Extensions;
using SharedLib.Embedding;
using CachingEmbeddingService = MemoryIndexer.Services.CachingEmbeddingService;

// Load .env file
var envPaths = new[] {
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env")
};
foreach (var path in envPaths.Where(File.Exists))
{
    DotNetEnv.Env.Load(path);
    Console.WriteLine($"[ENV] Loaded: {path}");
    break;
}

// Configuration
var gpuStackUrl = Environment.GetEnvironmentVariable("GPUSTACK_URL");
var gpuStackApiKey = Environment.GetEnvironmentVariable("GPUSTACK_APIKEY");
var gpuStackModel = Environment.GetEnvironmentVariable("GPUSTACK_MODEL") ?? "gpt-oss-20b";
var gpuStackEmbedModel = Environment.GetEnvironmentVariable("GPUSTACK_EMBED_MODEL");
var useGpuStackChat = !string.IsNullOrWhiteSpace(gpuStackUrl) && !string.IsNullOrWhiteSpace(gpuStackApiKey);
var useGpuStackEmbed = useGpuStackChat && !string.IsNullOrWhiteSpace(gpuStackEmbedModel);

Console.WriteLine($"[CONFIG] Chat LLM: {(useGpuStackChat ? gpuStackModel : "Echo Mode")}");
Console.WriteLine($"[CONFIG] Embedding: {(useGpuStackEmbed ? gpuStackEmbedModel : "LMSupply Local")}");

var builder = WebApplication.CreateBuilder(args);

// CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// Embedding service: register BEFORE AddMemoryIndexer (uses TryAddSingleton)
if (useGpuStackEmbed)
{
    // Use OpenAI-compatible GPUStack embedding via SharedLib, wrapped with caching
    var gpuStackEmbed = new OpenAIEmbeddingService(
        apiKey: gpuStackApiKey!,
        model: gpuStackEmbedModel!,
        dimensions: 1024,
        endpoint: new Uri(gpuStackUrl!.TrimEnd('/') + "/v1"));
    var cached = new CachingEmbeddingService(gpuStackEmbed, new EmbeddingCacheOptions
    {
        Ttl = TimeSpan.FromMinutes(30),
        MaxSize = 5000
    });
    builder.Services.AddSingleton<IEmbeddingService>(cached);
}

// Memory Indexer services
builder.Services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Storage.ConnectionString = "chat_memories.db";
    options.Embedding.Dimensions = 1024;
    options.Storage.VectorDimensions = 1024;

    if (!useGpuStackEmbed)
    {
        // Local embedding (LMSupply.Embedder)
        options.Embedding.Provider = EmbeddingProvider.Local;
        options.Embedding.Model = "bge-large-en-v1.5";
    }
});

// HTTP client for LLM
if (useGpuStackChat)
{
    builder.Services.AddHttpClient("GpuStack", client =>
    {
        client.BaseAddress = new Uri(gpuStackUrl!.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {gpuStackApiKey}");
    });
}

var app = builder.Build();
app.UseCors();

// Services
var memoryService = app.Services.GetRequiredService<MemoryService>();
var memoryStore = app.Services.GetRequiredService<IMemoryStore>();
var httpClientFactory = app.Services.GetService<IHttpClientFactory>();

// Simple in-memory storage for users and sessions
var users = new Dictionary<string, UserInfo>();
var sessions = new Dictionary<string, ChatSession>();

// API Endpoints
app.MapGet("/api/health", () =>
{
    Console.WriteLine("[API] GET /api/health");
    return Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow });
});

// === User Management ===
app.MapGet("/api/users", () =>
{
    Console.WriteLine($"[API] GET /api/users -> {users.Count} users");
    return Results.Ok(users.Values.OrderByDescending(u => u.LastActive));
});

app.MapPost("/api/users", (CreateUserRequest request) =>
{
    var userId = Guid.NewGuid().ToString("N")[..8];
    var user = new UserInfo
    {
        Id = userId,
        Name = request.Name,
        CreatedAt = DateTime.UtcNow,
        LastActive = DateTime.UtcNow
    };
    users[userId] = user;
    Console.WriteLine($"[API] POST /api/users -> Created: {user.Name} ({userId})");
    return Results.Ok(user);
});

app.MapGet("/api/users/{userId}", (string userId) =>
{
    if (!users.TryGetValue(userId, out var user))
        return Results.NotFound(new { error = "User not found" });

    Console.WriteLine($"[API] GET /api/users/{userId} -> {user.Name}");
    return Results.Ok(user);
});

// === Session Management ===
app.MapGet("/api/users/{userId}/sessions", (string userId) =>
{
    if (!users.ContainsKey(userId))
        return Results.NotFound(new { error = "User not found" });

    var userSessions = sessions.Values
        .Where(s => s.UserId == userId)
        .OrderByDescending(s => s.LastMessage)
        .ToList();

    Console.WriteLine($"[API] GET /api/users/{userId}/sessions -> {userSessions.Count} sessions");
    return Results.Ok(userSessions);
});

app.MapPost("/api/users/{userId}/sessions", (string userId, CreateSessionRequest? request) =>
{
    if (!users.TryGetValue(userId, out var user))
        return Results.NotFound(new { error = "User not found" });

    var sessionId = Guid.NewGuid().ToString("N")[..8];
    var session = new ChatSession
    {
        Id = sessionId,
        UserId = userId,
        Title = request?.Title ?? $"Chat {DateTime.Now:MMdd HH:mm}",
        CreatedAt = DateTime.UtcNow,
        LastMessage = DateTime.UtcNow
    };
    sessions[sessionId] = session;
    user.LastActive = DateTime.UtcNow;

    Console.WriteLine($"[API] POST /api/users/{userId}/sessions -> Created: {session.Title} ({sessionId})");
    return Results.Ok(session);
});

app.MapGet("/api/sessions/{sessionId}", (string sessionId) =>
{
    if (!sessions.TryGetValue(sessionId, out var session))
        return Results.NotFound(new { error = "Session not found" });

    Console.WriteLine($"[API] GET /api/sessions/{sessionId} -> {session.Title}");
    return Results.Ok(session);
});

app.MapDelete("/api/sessions/{sessionId}", async (string sessionId) =>
{
    if (!sessions.TryGetValue(sessionId, out var session))
        return Results.NotFound(new { error = "Session not found" });

    // Delete session memories
    var allMemories = await memoryStore.GetAllAsync(session.UserId);
    var sessionMemories = allMemories.Where(m => m.SessionId == sessionId).ToList();
    foreach (var m in sessionMemories)
        await memoryStore.DeleteAsync(m.Id);

    sessions.Remove(sessionId);
    Console.WriteLine($"[API] DELETE /api/sessions/{sessionId} -> Deleted {sessionMemories.Count} memories");
    return Results.Ok(new { deleted = sessionMemories.Count });
});

// === Chat ===
app.MapPost("/api/chat", async (ChatRequest request) =>
{
    Console.WriteLine($"[API] POST /api/chat - Session: {request.SessionId}, Message: {request.Message}");

    if (!sessions.TryGetValue(request.SessionId, out var session))
        return Results.NotFound(new { error = "Session not found" });

    if (!users.TryGetValue(session.UserId, out var user))
        return Results.NotFound(new { error = "User not found" });

    var userId = session.UserId;
    var sessionId = request.SessionId;

    // Update timestamps
    session.LastMessage = DateTime.UtcNow;
    session.MessageCount++;
    user.LastActive = DateTime.UtcNow;

    // Store user message
    try
    {
        await memoryService.StoreAsync(userId, $"[User]: {request.Message}", MemoryType.Episodic, sessionId, 0.7f);
        Console.WriteLine($"[MEMORY] Stored user message");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MEMORY] Store error: {ex.Message}");
    }

    // Recall memories
    var memories = new List<MemorySearchResult>();
    try
    {
        var sessionMemories = await memoryService.RecallAsync(userId, request.Message, 3, sessionId);
        var longTermMemories = await memoryService.RecallAsync(userId, request.Message, 3);
        memories.AddRange(sessionMemories);
        foreach (var m in longTermMemories.Where(m => !memories.Any(x => x.Memory.Id == m.Memory.Id)))
            memories.Add(m);
        memories = memories.OrderByDescending(m => m.Score).Take(5).ToList();
        Console.WriteLine($"[MEMORY] Recalled {memories.Count} memories");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MEMORY] Recall error: {ex.Message}");
    }

    // Build context
    var context = string.Join("\n", memories.Select(m =>
    {
        var age = DateTime.UtcNow - m.Memory.CreatedAt;
        var ageStr = age.TotalMinutes < 60 ? $"{age.TotalMinutes:F0}m" :
                     age.TotalHours < 24 ? $"{age.TotalHours:F0}h" : $"{age.TotalDays:F0}d";
        return $"[{m.Memory.Type}, {ageStr} ago, score:{m.Score:F2}] {m.Memory.Content}";
    }));

    // Generate response
    string response;
    if (useGpuStackChat && httpClientFactory != null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("GpuStack");
            var chatRequest = new
            {
                model = gpuStackModel,
                messages = new[]
                {
                    new { role = "system", content = $"You are a helpful AI assistant talking to {user.Name}.\n\nRelevant memories:\n{context}" },
                    new { role = "user", content = request.Message }
                },
                max_tokens = 500,
                temperature = 0.7
            };
            var result = await client.PostAsJsonAsync("chat/completions", chatRequest);
            result.EnsureSuccessStatusCode();
            var chatResponse = await result.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            response = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response";
            Console.WriteLine($"[LLM] Generated response: {response[..Math.Min(50, response.Length)]}...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM] Error: {ex.Message}");
            response = $"[LLM Error: {ex.Message}]";
        }
    }
    else
    {
        response = $"[Echo Mode]\nYour message: {request.Message}\n\nContext:\n{context}";
        Console.WriteLine($"[ECHO] Response generated");
    }

    // Store assistant response
    try
    {
        await memoryService.StoreAsync(userId, $"[Assistant]: {response}", MemoryType.Episodic, sessionId, 0.6f);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MEMORY] Store response error: {ex.Message}");
    }

    // Extract semantic facts
    var factPatterns = new[] { "my name is", "i am", "i work", "i like", "i love", "i have", "i live", "my favorite" };
    if (factPatterns.Any(p => request.Message.ToLower().Contains(p)))
    {
        try
        {
            await memoryService.StoreAsync(userId, $"[Fact about {user.Name}]: {request.Message}", MemoryType.Semantic, null, 0.9f);
            Console.WriteLine($"[MEMORY] Extracted semantic fact");
        }
        catch { }
    }

    // Update session title from first message
    if (session.MessageCount == 1 && request.Message.Length > 3)
    {
        session.Title = request.Message.Length > 30
            ? request.Message[..30] + "..."
            : request.Message;
    }

    return Results.Ok(new
    {
        response,
        memoriesUsed = memories.Count,
        memories = memories.Select(m => new
        {
            content = m.Memory.Content,
            type = m.Memory.Type.ToString(),
            score = m.Score
        })
    });
});

// === Status ===
app.MapGet("/api/users/{userId}/status", async (string userId) =>
{
    Console.WriteLine($"[API] GET /api/users/{userId}/status");

    if (!users.TryGetValue(userId, out var user))
        return Results.NotFound(new { error = "User not found" });

    var allMemories = await memoryStore.GetAllAsync(userId);

    var byType = allMemories.GroupBy(m => m.Type).ToDictionary(g => g.Key.ToString(), g => g.Count());
    var bySession = allMemories.GroupBy(m => m.SessionId ?? "long-term").ToDictionary(g => g.Key, g => g.Count());
    var recent = allMemories.OrderByDescending(m => m.CreatedAt).Take(10).Select(m => new
    {
        content = m.Content.Length > 100 ? m.Content[..100] + "..." : m.Content,
        type = m.Type.ToString(),
        createdAt = m.CreatedAt,
        importance = m.ImportanceScore
    });

    return Results.Ok(new
    {
        user = user.Name,
        total = allMemories.Count,
        byType,
        bySession,
        recent,
        config = new
        {
            embedding = useGpuStackEmbed ? gpuStackEmbedModel : "LMSupply Local (bge-large-en-v1.5)",
            chatLlm = useGpuStackChat ? gpuStackModel : "Echo Mode"
        }
    });
});

app.MapDelete("/api/users/{userId}/memories", async (string userId) =>
{
    Console.WriteLine($"[API] DELETE /api/users/{userId}/memories");

    if (!users.ContainsKey(userId))
        return Results.NotFound(new { error = "User not found" });

    var all = await memoryStore.GetAllAsync(userId);
    foreach (var m in all)
        await memoryStore.DeleteAsync(m.Id);
    Console.WriteLine($"[MEMORY] Deleted {all.Count} memories for user {userId}");
    return Results.Ok(new { deleted = all.Count });
});

Console.WriteLine($"[SERVER] Starting on http://localhost:5000");
app.Run("http://localhost:5000");

// Models
record CreateUserRequest(string Name);
record CreateSessionRequest(string? Title);
record ChatRequest(string SessionId, string Message);

class UserInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActive { get; set; }
}

class ChatSession
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessage { get; set; }
    public int MessageCount { get; set; }
}

class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }
}

class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }
}

class ChatMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
