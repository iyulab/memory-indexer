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

// 3-Tier memory services (Buffer + Short + Long)
var buffer = app.Services.GetRequiredService<IBuffer>();
var shortTermMemory = app.Services.GetRequiredService<IShortTermMemory>();

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

// === Chat (Non-Streaming) ===
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

    // Recall memories (3-Tier: Buffer + Short + Long)
    var (contextParts, memories, context) = await RecallMemoriesAsync(userId, sessionId, request.Message);

    // Generate response
    string response;
    if (useGpuStackChat && httpClientFactory != null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("GpuStack");
            // NOTE: No conversation history - only recalled memories + current message
            // This demonstrates memory-indexer's purpose: replace full history with intelligent recall
            var systemPrompt = $"""
                You are a helpful AI assistant talking to {user.Name}.

                ## Conversation Context (from memory)
                The following is recalled from your memory about this conversation. Use this context to maintain continuity:

                {context}

                ## Instructions
                - Continue the conversation naturally based on the above context
                - If a game or activity is in progress, stay in that context
                - Respond to the user's current message while being aware of the conversation history
                """;
            var chatReq = new
            {
                model = gpuStackModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = request.Message }
                },
                max_tokens = 8192,  // Sufficient for reasoning + content
                temperature = 0.7
            };
            var result = await client.PostAsJsonAsync("chat/completions", chatReq);
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

    // Store conversation (fire-and-forget)
    StoreConversationAsync(userId, sessionId, request.Message, response, user.Name);

    // Update session title from first message
    if (session.MessageCount == 1 && request.Message.Length > 3)
    {
        session.Title = request.Message.Length > 30 ? request.Message[..30] + "..." : request.Message;
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

// === Chat (Streaming with SSE) ===
app.MapGet("/api/chat/stream", async (HttpContext httpContext, string sessionId, string message) =>
{
    Console.WriteLine($"[API] GET /api/chat/stream - Session: {sessionId}, Message: {message}");

    if (!sessions.TryGetValue(sessionId, out var session))
    {
        httpContext.Response.StatusCode = 404;
        await httpContext.Response.WriteAsJsonAsync(new { error = "Session not found" });
        return;
    }

    if (!users.TryGetValue(session.UserId, out var user))
    {
        httpContext.Response.StatusCode = 404;
        await httpContext.Response.WriteAsJsonAsync(new { error = "User not found" });
        return;
    }

    var userId = session.UserId;

    // Setup SSE
    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";
    httpContext.Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering
    await httpContext.Response.Body.FlushAsync(); // Ensure headers are sent immediately

    async Task SendEventAsync(string type, object data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { type, data });
        await httpContext.Response.WriteAsync($"data: {json}\n\n");
        await httpContext.Response.Body.FlushAsync();
    }

    // Update timestamps
    session.LastMessage = DateTime.UtcNow;
    session.MessageCount++;
    user.LastActive = DateTime.UtcNow;

    // === METRICS ===
    var requestStartTime = DateTime.UtcNow;
    var recallStartTime = DateTime.UtcNow;
    var llmStartTime = DateTime.MinValue;
    var llmFirstTokenTime = DateTime.MinValue;
    var userTokens = message.Length / 4; // Rough estimate: 4 chars per token

    // === THINKING PHASE ===
    await SendEventAsync("thinking", new { step = "start" });
    await Task.Delay(100); // Brief delay to allow UI to show thinking state

    var contextParts = new List<string>();
    var memories = new List<MemorySearchResult>();

    // T0: Buffer
    try
    {
        var bufferItems = await buffer.GetPendingAsync(userId);
        var sessionBufferItems = bufferItems.Where(b => b.SessionId == sessionId).ToList();
        if (sessionBufferItems.Count > 0)
        {
            var recentBuffer = sessionBufferItems
                .TakeLast(5)
                .Select(b => $"[Buffer, {(DateTime.UtcNow - b.Timestamp).TotalSeconds:F0}s ago] {b.Content}");
            contextParts.AddRange(recentBuffer);
        }
        await SendEventAsync("thinking", new { step = "buffer", count = sessionBufferItems.Count });
        await Task.Delay(150); // Brief delay to show buffer state
        Console.WriteLine($"[MEMORY] Buffer (T0): {sessionBufferItems.Count} items");
    }
    catch (Exception ex)
    {
        await SendEventAsync("thinking", new { step = "buffer", error = ex.Message });
    }

    // T1: Short term
    try
    {
        var shortTermItems = await shortTermMemory.GetAllAsync();
        if (shortTermItems.Count > 0)
        {
            var workingMemory = shortTermItems.Select(m =>
            {
                var age = DateTime.UtcNow - m.CreatedAt;
                var ageStr = age.TotalMinutes < 60 ? $"{age.TotalMinutes:F0}m" :
                             age.TotalHours < 24 ? $"{age.TotalHours:F0}h" : $"{age.TotalDays:F0}d";
                return $"[WorkingMemory, {m.Type}, {ageStr} ago] {m.Content}";
            });
            contextParts.AddRange(workingMemory);
        }
        await SendEventAsync("thinking", new { step = "short_term", count = shortTermItems.Count });
        await Task.Delay(150); // Brief delay to show short-term state
        Console.WriteLine($"[MEMORY] Short term (T1): {shortTermItems.Count} items");
    }
    catch (Exception ex)
    {
        await SendEventAsync("thinking", new { step = "short_term", error = ex.Message });
    }

    // T2: Long term
    try
    {
        var sessionMemories = await memoryService.RecallAsync(userId, message, 3, sessionId);
        var longTermMemories = await memoryService.RecallAsync(userId, message, 3);
        memories.AddRange(sessionMemories);
        foreach (var m in longTermMemories.Where(m => !memories.Any(x => x.Memory.Id == m.Memory.Id)))
            memories.Add(m);
        memories = memories.OrderByDescending(m => m.Score).Take(5).ToList();

        contextParts.AddRange(memories.Select(m =>
        {
            var age = DateTime.UtcNow - m.Memory.CreatedAt;
            var ageStr = age.TotalMinutes < 60 ? $"{age.TotalMinutes:F0}m" :
                         age.TotalHours < 24 ? $"{age.TotalHours:F0}h" : $"{age.TotalDays:F0}d";
            return $"[LongTerm, {m.Memory.Type}, {ageStr} ago, score:{m.Score:F2}] {m.Memory.Content}";
        }));

        await SendEventAsync("thinking", new
        {
            step = "long_term",
            count = memories.Count,
            memories = memories.Select(m => new { content = m.Memory.Content[..Math.Min(50, m.Memory.Content.Length)], score = m.Score })
        });
        await Task.Delay(200); // Brief delay to show long-term state
        Console.WriteLine($"[MEMORY] Long term (T2): {memories.Count} memories");
    }
    catch (Exception ex)
    {
        await SendEventAsync("thinking", new { step = "long_term", error = ex.Message });
    }

    var context = string.Join("\n", contextParts);
    var recallEndTime = DateTime.UtcNow;
    var recallDurationMs = (int)(recallEndTime - recallStartTime).TotalMilliseconds;
    await SendEventAsync("thinking", new { step = "done", totalContext = contextParts.Count, recallMs = recallDurationMs });

    // === GENERATION PHASE ===
    llmStartTime = DateTime.UtcNow;
    var responseBuilder = new System.Text.StringBuilder();

    if (useGpuStackChat && httpClientFactory != null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("GpuStack");
            // NOTE: No conversation history - only recalled memories + current message
            // This demonstrates memory-indexer's purpose: replace full history with intelligent recall
            var systemPrompt = $"""
                You are a helpful AI assistant talking to {user.Name}.

                ## Conversation Context (from memory)
                The following is recalled from your memory about this conversation. Use this context to maintain continuity:

                {context}

                ## Instructions
                - Continue the conversation naturally based on the above context
                - If a game or activity is in progress, stay in that context
                - Respond to the user's current message while being aware of the conversation history
                """;
            var chatReq = new
            {
                model = gpuStackModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = message }
                },
                max_tokens = 8192,  // Sufficient for reasoning + content
                temperature = 0.7,
                stream = true
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(chatReq)
            };

            var streamResponse = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            streamResponse.EnsureSuccessStatusCode();

            await using var stream = await streamResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var lineCount = 0;
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                lineCount++;

                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line[6..];
                if (data == "[DONE]") break;

                try
                {
                    var chunk = System.Text.Json.JsonSerializer.Deserialize<StreamChunk>(data);
                    var choiceCount = chunk?.Choices?.Count ?? 0;
                    var firstChoice = chunk?.Choices?.FirstOrDefault();
                    var deltaObj = firstChoice?.Delta ?? firstChoice?.Message;

                    // Try delta first (streaming), fallback to message (non-streaming chunk)
                    var content = deltaObj?.Content;
                    var reasoning = deltaObj?.Reasoning ?? deltaObj?.ReasoningContent;

                    // Send reasoning event
                    if (!string.IsNullOrEmpty(reasoning))
                    {
                        await SendEventAsync("reasoning", new { delta = reasoning });
                    }

                    // Send content event
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (llmFirstTokenTime == DateTime.MinValue)
                            llmFirstTokenTime = DateTime.UtcNow;
                        responseBuilder.Append(content);
                        await SendEventAsync("content", new { delta = content });
                    }
                }
                catch
                {
                    // Ignore parse errors for malformed chunks
                }
            }

            Console.WriteLine($"[LLM] Streamed response: {responseBuilder.Length} chars");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM] Stream error: {ex.Message}");
            await SendEventAsync("error", new { message = ex.Message });
            responseBuilder.Append($"[LLM Error: {ex.Message}]");
        }
    }
    else
    {
        // Echo mode - simulate streaming
        var echoResponse = $"[Echo Mode]\nYour message: {message}\n\nContext:\n{context}";
        foreach (var chunk in echoResponse.Chunk(20))
        {
            var delta = new string(chunk);
            responseBuilder.Append(delta);
            await SendEventAsync("content", new { delta });
            await Task.Delay(30); // Simulate streaming delay
        }
        Console.WriteLine($"[ECHO] Streamed response");
    }

    var response = responseBuilder.ToString();

    // Store conversation (fire-and-forget)
    StoreConversationAsync(userId, sessionId, message, response, user.Name);

    // Update session title
    if (session.MessageCount == 1 && message.Length > 3)
    {
        session.Title = message.Length > 30 ? message[..30] + "..." : message;
    }

    // Calculate metrics
    var llmEndTime = DateTime.UtcNow;
    var aiTokens = response.Length / 4; // Rough estimate
    var contextTokens = context.Length / 4;
    var llmDurationMs = (int)(llmEndTime - llmStartTime).TotalMilliseconds;
    var ttftMs = llmFirstTokenTime != DateTime.MinValue
        ? (int)(llmFirstTokenTime - llmStartTime).TotalMilliseconds
        : 0;
    var totalDurationMs = (int)(llmEndTime - requestStartTime).TotalMilliseconds;

    // Send done event with metrics
    await SendEventAsync("done", new
    {
        memoriesUsed = memories.Count,
        totalLength = response.Length,
        metrics = new
        {
            userTokens,
            aiTokens,
            contextTokens,
            totalTokens = userTokens + aiTokens + contextTokens,
            recallMs = recallDurationMs,
            llmMs = llmDurationMs,
            ttftMs,  // Time to first token
            totalMs = totalDurationMs
        }
    });
});

// Helper: Recall memories from all tiers
async Task<(List<string> contextParts, List<MemorySearchResult> memories, string context)> RecallMemoriesAsync(
    string userId, string sessionId, string query)
{
    var contextParts = new List<string>();
    var memories = new List<MemorySearchResult>();

    // T0: Buffer - use Role field for speaker identification
    try
    {
        var bufferItems = await buffer.GetPendingAsync(userId);
        if (bufferItems.Count > 0)
        {
            var recentBuffer = bufferItems
                .Where(b => b.SessionId == sessionId)
                .TakeLast(5)
                .Select(b =>
                {
                    var role = b.Role ?? "unknown";
                    var roleLabel = role == "user" ? "User" : role == "assistant" ? "Assistant" : role;
                    var ageSeconds = (DateTime.UtcNow - b.Timestamp).TotalSeconds;
                    return $"[{roleLabel}, {ageSeconds:F0}s ago] {b.Content}";
                });
            contextParts.AddRange(recentBuffer);
            Console.WriteLine($"[MEMORY] Buffer (T0): {bufferItems.Count} items");
        }
    }
    catch (Exception ex) { Console.WriteLine($"[MEMORY] Buffer error: {ex.Message}"); }

    // T1: Short term
    try
    {
        var shortTermItems = await shortTermMemory.GetAllAsync();
        if (shortTermItems.Count > 0)
        {
            var workingMemory = shortTermItems.Select(m =>
            {
                var age = DateTime.UtcNow - m.CreatedAt;
                var ageStr = age.TotalMinutes < 60 ? $"{age.TotalMinutes:F0}m" :
                             age.TotalHours < 24 ? $"{age.TotalHours:F0}h" : $"{age.TotalDays:F0}d";
                return $"[WorkingMemory, {m.Type}, {ageStr} ago] {m.Content}";
            });
            contextParts.AddRange(workingMemory);
            Console.WriteLine($"[MEMORY] Short term (T1): {shortTermItems.Count} items");
        }
    }
    catch (Exception ex) { Console.WriteLine($"[MEMORY] Short term error: {ex.Message}"); }

    // T2: Long term
    try
    {
        var sessionMemories = await memoryService.RecallAsync(userId, query, 3, sessionId);
        var longTermMemories = await memoryService.RecallAsync(userId, query, 3);
        memories.AddRange(sessionMemories);
        foreach (var m in longTermMemories.Where(m => !memories.Any(x => x.Memory.Id == m.Memory.Id)))
            memories.Add(m);
        memories = memories.OrderByDescending(m => m.Score).Take(5).ToList();

        contextParts.AddRange(memories.Select(m =>
        {
            var age = DateTime.UtcNow - m.Memory.CreatedAt;
            var ageStr = age.TotalMinutes < 60 ? $"{age.TotalMinutes:F0}m" :
                         age.TotalHours < 24 ? $"{age.TotalHours:F0}h" : $"{age.TotalDays:F0}d";
            return $"[LongTerm, {m.Memory.Type}, {ageStr} ago, score:{m.Score:F2}] {m.Memory.Content}";
        }));
        Console.WriteLine($"[MEMORY] Long term (T2): {memories.Count} memories");
    }
    catch (Exception ex) { Console.WriteLine($"[MEMORY] Long term recall error: {ex.Message}"); }

    var context = string.Join("\n", contextParts);
    Console.WriteLine($"[MEMORY] Total context: {contextParts.Count} entries");

    return (contextParts, memories, context);
}

// Helper: Store conversation (fire-and-forget)
void StoreConversationAsync(string userId, string sessionId, string userMsg, string assistantMsg, string userName)
{
    _ = Task.Run(async () =>
    {
        try
        {
            // Buffer (T0) - role is stored in SensoryMemory.Role field
            await buffer.EnqueueAsync(userMsg, userId, sessionId, role: "user");
            await buffer.EnqueueAsync(assistantMsg, userId, sessionId, role: "assistant");
            Console.WriteLine($"[MEMORY] Buffered conversation (T0)");

            // Long term (T2) - include role in content for vector search
            await memoryService.StoreAsync(userId, $"[User] {userMsg}", MemoryType.Episodic, sessionId, 0.7f);
            await memoryService.StoreAsync(userId, $"[Assistant] {assistantMsg}", MemoryType.Episodic, sessionId, 0.6f);
            Console.WriteLine($"[MEMORY] Stored conversation (T2)");

            // Extract semantic facts
            var factPatterns = new[] { "my name is", "i am", "i work", "i like", "i love", "i have", "i live", "my favorite" };
            if (factPatterns.Any(p => userMsg.ToLower().Contains(p)))
            {
                var factContent = $"[Fact about {userName}]: {userMsg}";
                await memoryService.StoreAsync(userId, factContent, MemoryType.Semantic, null, 0.9f);
                Console.WriteLine($"[MEMORY] Extracted semantic fact");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MEMORY] Store error: {ex.Message}");
        }
    });
}

// === Status ===
app.MapGet("/api/users/{userId}/status", async (string userId, string? sessionId) =>
{
    Console.WriteLine($"[API] GET /api/users/{userId}/status (session: {sessionId ?? "all"})");

    if (!users.TryGetValue(userId, out var user))
        return Results.NotFound(new { error = "User not found" });

    // Get tier information
    var bufferItems = await buffer.GetPendingAsync(userId);
    var sessionBufferItems = sessionId != null
        ? bufferItems.Where(b => b.SessionId == sessionId).ToList()
        : bufferItems.ToList();

    var shortTermItems = await shortTermMemory.GetAllAsync();

    var allMemories = await memoryStore.GetAllAsync(userId);
    var sessionMemories = sessionId != null
        ? allMemories.Where(m => m.SessionId == sessionId).ToList()
        : allMemories;
    var userMemories = allMemories.Where(m => string.IsNullOrEmpty(m.SessionId)).ToList();

    // Tier stats
    var tiers = new
    {
        buffer = new
        {
            count = sessionBufferItems.Count,
            totalTokens = sessionBufferItems.Sum(b => b.Content.Length / 4), // rough estimate
            items = sessionBufferItems.TakeLast(3).Select(b => new
            {
                content = b.Content.Length > 60 ? b.Content[..60] + "..." : b.Content,
                ageSeconds = (int)(DateTime.UtcNow - b.Timestamp).TotalSeconds
            })
        },
        shortTerm = new
        {
            count = shortTermItems.Count,
            capacity = 7, // Miller's law
            items = shortTermItems.TakeLast(5).Select(m => new
            {
                content = m.Content.Length > 60 ? m.Content[..60] + "..." : m.Content,
                type = m.Type.ToString(),
                ageMinutes = (int)(DateTime.UtcNow - m.CreatedAt).TotalMinutes
            })
        },
        longTerm = new
        {
            count = sessionMemories.Count,
            sessionCount = sessionId != null ? sessionMemories.Count : allMemories.Count(m => !string.IsNullOrEmpty(m.SessionId)),
            userCount = userMemories.Count
        },
        archive = new
        {
            count = 0, // Archive not implemented yet
            confirmed = 0
        }
    };

    // By type
    var byType = allMemories.GroupBy(m => m.Type).ToDictionary(g => g.Key.ToString(), g => g.Count());

    // By scope
    var byScope = new
    {
        session = allMemories.Count(m => !string.IsNullOrEmpty(m.SessionId)),
        user = userMemories.Count
    };

    // Recent memories with tier info
    var recent = allMemories.OrderByDescending(m => m.CreatedAt).Take(8).Select(m => new
    {
        content = m.Content.Length > 80 ? m.Content[..80] + "..." : m.Content,
        type = m.Type.ToString(),
        tier = "T2", // All stored memories are Long-term (T2)
        scope = string.IsNullOrEmpty(m.SessionId) ? "User" : "Session",
        createdAt = m.CreatedAt,
        importance = m.ImportanceScore
    });

    return Results.Ok(new
    {
        user = user.Name,
        total = allMemories.Count,
        tiers,
        byType,
        byScope,
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

    var deleted = await memoryStore.DeleteByUserAsync(userId, hardDelete: true);
    Console.WriteLine($"[MEMORY] Deleted {deleted} memories for user {userId}");
    return Results.Ok(new { deleted });
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


class StreamChunk
{
    [JsonPropertyName("choices")]
    public List<StreamChoice>? Choices { get; set; }
}

class StreamChoice
{
    [JsonPropertyName("delta")]
    public StreamDelta? Delta { get; set; }

    [JsonPropertyName("message")]
    public StreamDelta? Message { get; set; }
}

class StreamDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }
}
