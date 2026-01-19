using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LMSupply.Embedder;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using MemoryIndexer.Sdk.Extensions;
using SharedLib.Embedding;
using CachingEmbeddingService = MemoryIndexer.Services.CachingEmbeddingService;

// Context Budget API models
using ContextBudget = MemoryIndexer.Models.ContextBudget;
using ContextRequest = MemoryIndexer.Models.ContextRequest;

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

// Configuration - GPUStack (priority) or OpenAI (fallback)
var gpuStackUrl = Environment.GetEnvironmentVariable("GPUSTACK_URL");
var gpuStackApiKey = Environment.GetEnvironmentVariable("GPUSTACK_APIKEY");
var gpuStackModel = Environment.GetEnvironmentVariable("GPUSTACK_MODEL") ?? "gpt-oss-20b";
var gpuStackEmbedModel = Environment.GetEnvironmentVariable("GPUSTACK_EMBED_MODEL");

var openaiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var openaiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
var openaiEmbedModel = Environment.GetEnvironmentVariable("OPENAI_EMBED_MODEL") ?? "text-embedding-3-small";

// Determine which provider to use
var useGpuStack = !string.IsNullOrWhiteSpace(gpuStackUrl) && !string.IsNullOrWhiteSpace(gpuStackApiKey);
var useOpenAI = !useGpuStack && !string.IsNullOrWhiteSpace(openaiApiKey);

// Effective configuration
var (llmEndpoint, llmApiKey, llmModel) = useGpuStack
    ? (gpuStackUrl!.TrimEnd('/') + "/v1", gpuStackApiKey!, gpuStackModel)
    : useOpenAI
        ? ("https://api.openai.com/v1", openaiApiKey!, openaiModel)
        : (null, null, null);

var useExternalLlm = llmEndpoint != null;

// Embedding configuration
var useGpuStackEmbed = useGpuStack && !string.IsNullOrWhiteSpace(gpuStackEmbedModel);
var useOpenAIEmbed = !useGpuStackEmbed && useOpenAI;
var useExternalEmbed = useGpuStackEmbed || useOpenAIEmbed;

var (embedEndpoint, embedApiKey, embedModel, embedDimensions) = useGpuStackEmbed
    ? (gpuStackUrl!.TrimEnd('/') + "/v1", gpuStackApiKey!, gpuStackEmbedModel!, 1024)
    : useOpenAIEmbed
        ? ("https://api.openai.com/v1", openaiApiKey!, openaiEmbedModel, 1536)
        : (null, null, null, 1024);

// Context Budget API configuration
var contextStrategy = Environment.GetEnvironmentVariable("CONTEXT_STRATEGY") ?? "RecentHeavy";
var contextBudgetTokens = int.TryParse(Environment.GetEnvironmentVariable("CONTEXT_BUDGET_TOKENS"), out var tokens) ? tokens : 2000;

// Log configuration
var llmProvider = useGpuStack ? "GPUStack" : useOpenAI ? "OpenAI" : "Echo Mode";
var embedProvider = useGpuStackEmbed ? "GPUStack" : useOpenAIEmbed ? "OpenAI" : "LMSupply Local";
Console.WriteLine($"[CONFIG] Chat LLM: {llmProvider} ({llmModel ?? "N/A"})");
Console.WriteLine($"[CONFIG] Embedding: {embedProvider} ({embedModel ?? "bge-large-en-v1.5"})");
Console.WriteLine($"[CONFIG] Context Strategy: {contextStrategy} ({contextBudgetTokens} tokens)");

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
if (useExternalEmbed)
{
    // Use OpenAI-compatible embedding via SharedLib (GPUStack or OpenAI), wrapped with caching
    var externalEmbed = new OpenAIEmbeddingService(
        apiKey: embedApiKey!,
        model: embedModel!,
        dimensions: embedDimensions,
        endpoint: new Uri(embedEndpoint!));
    var cached = new CachingEmbeddingService(externalEmbed, new EmbeddingCacheOptions
    {
        Ttl = TimeSpan.FromMinutes(30),
        MaxSize = 5000
    });
    builder.Services.AddSingleton<IEmbeddingService>(cached);
}
else
{
    // Local embedding using LMSupply directly
    var localEmbedModel = await LocalEmbedder.LoadAsync("bge-large-en-v1.5");
    embedDimensions = localEmbedModel.Dimensions;
    builder.Services.AddSingleton<IEmbeddingService>(new LMSupplyEmbeddingService(localEmbedModel));
}

// Memory Indexer services with SQLite persistent storage
builder.Services.AddMemoryIndexer(options =>
{
    options.Storage.ConnectionString = "chat_memories.db";
    options.Embedding.Dimensions = embedDimensions;
    options.Storage.VectorDimensions = embedDimensions;
}).WithSqliteVec();

// HTTP client for LLM (OpenAI-compatible API)
if (useExternalLlm)
{
    builder.Services.AddHttpClient("LlmClient", client =>
    {
        client.BaseAddress = new Uri(llmEndpoint! + "/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {llmApiKey}");
    });
}

var app = builder.Build();
app.UseCors();

// Static files for SPA (production)
app.UseStaticFiles();

// Services
var memoryService = app.Services.GetRequiredService<MemoryService>();
var memoryStore = app.Services.GetRequiredService<IMemoryStore>();
var httpClientFactory = app.Services.GetService<IHttpClientFactory>();

// 3-Tier memory services (Buffer + Short + Long)
var buffer = app.Services.GetRequiredService<IBuffer>();
var shortTermMemory = app.Services.GetRequiredService<IShortTermMemory>();

// Context Budget API (v0.9.0) - Token-aware context building
var contextBuilder = app.Services.GetRequiredService<IContextBuilder>();
var tokenCounter = app.Services.GetRequiredService<ITokenCounter>();

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

    // Recall memories using Context Budget API
    var (contextBundle, context) = await RecallMemoriesAsync(userId, sessionId, request.Message);

    // Generate response
    string response;
    if (useExternalLlm && httpClientFactory != null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("LlmClient");
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
                model = llmModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = request.Message }
                },
                max_completion_tokens = 4096,
                temperature = 0.7
            };
            var result = await client.PostAsJsonAsync("chat/completions", chatReq);
            if (!result.IsSuccessStatusCode)
            {
                var errorBody = await result.Content.ReadAsStringAsync();
                Console.WriteLine($"[LLM] API Error {result.StatusCode}: {errorBody}");
                throw new HttpRequestException($"LLM API failed: {result.StatusCode} - {errorBody[..Math.Min(200, errorBody.Length)]}");
            }
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
        memoriesUsed = contextBundle.ItemCount,
        contextTokens = contextBundle.TotalTokens,
        breakdown = new
        {
            recent = contextBundle.Breakdown.RecentTokens,
            semantic = contextBundle.Breakdown.SemanticTokens,
            episodic = contextBundle.Breakdown.EpisodicTokens,
            fact = contextBundle.Breakdown.FactTokens
        },
        items = contextBundle.Items.Select(i => new
        {
            content = i.Content.Length > 100 ? i.Content[..100] + "..." : i.Content,
            source = i.Source.ToString(),
            tokens = i.Tokens,
            score = i.Score,
            role = i.Role
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

    // === THINKING PHASE - Using Context Budget API ===
    await SendEventAsync("thinking", new { step = "start", strategy = contextStrategy, budget = contextBudgetTokens });
    await Task.Delay(100); // Brief delay to allow UI to show thinking state

    ContextBundle? contextBundle = null;
    var context = "";

    try
    {
        var request = new ContextRequest(
            UserId: userId,
            SessionId: sessionId,
            Query: message,
            Budget: new ContextBudget(TotalTokens: contextBudgetTokens)
        );

        await SendEventAsync("thinking", new { step = "building", strategy = contextStrategy });
        await Task.Delay(150);

        contextBundle = await contextBuilder.BuildAsync(request, contextStrategy);
        context = contextBundle.Content;

        await SendEventAsync("thinking", new
        {
            step = "done",
            strategy = contextStrategy,
            totalItems = contextBundle.ItemCount,
            totalTokens = contextBundle.TotalTokens,
            breakdown = new
            {
                recent = contextBundle.Breakdown.RecentTokens,
                semantic = contextBundle.Breakdown.SemanticTokens,
                episodic = contextBundle.Breakdown.EpisodicTokens,
                fact = contextBundle.Breakdown.FactTokens
            },
            items = contextBundle.Items.Take(5).Select(i => new
            {
                content = i.Content.Length > 50 ? i.Content[..50] + "..." : i.Content,
                source = i.Source.ToString(),
                tokens = i.Tokens
            })
        });

        Console.WriteLine($"[MEMORY] Context built: {contextBundle.ItemCount} items, {contextBundle.TotalTokens} tokens " +
            $"(R:{contextBundle.Breakdown.RecentTokens}/S:{contextBundle.Breakdown.SemanticTokens}/E:{contextBundle.Breakdown.EpisodicTokens}/F:{contextBundle.Breakdown.FactTokens})");
    }
    catch (Exception ex)
    {
        await SendEventAsync("thinking", new { step = "error", error = ex.Message });
        Console.WriteLine($"[MEMORY] Context build error: {ex.Message}");
    }

    var recallEndTime = DateTime.UtcNow;
    var recallDurationMs = (int)(recallEndTime - recallStartTime).TotalMilliseconds;

    // === GENERATION PHASE ===
    llmStartTime = DateTime.UtcNow;
    var responseBuilder = new System.Text.StringBuilder();

    if (useExternalLlm && httpClientFactory != null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("LlmClient");
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
                model = llmModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = message }
                },
                max_completion_tokens = 4096,
                temperature = 0.7,
                stream = true
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(chatReq)
            };

            var streamResponse = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            if (!streamResponse.IsSuccessStatusCode)
            {
                var errorBody = await streamResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[LLM] Stream API Error {streamResponse.StatusCode}: {errorBody}");
                throw new HttpRequestException($"LLM API failed: {streamResponse.StatusCode} - {errorBody[..Math.Min(200, errorBody.Length)]}");
            }

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
    var contextTokens = contextBundle?.TotalTokens ?? 0;
    var llmDurationMs = (int)(llmEndTime - llmStartTime).TotalMilliseconds;
    var ttftMs = llmFirstTokenTime != DateTime.MinValue
        ? (int)(llmFirstTokenTime - llmStartTime).TotalMilliseconds
        : 0;
    var totalDurationMs = (int)(llmEndTime - requestStartTime).TotalMilliseconds;

    // Send done event with metrics
    await SendEventAsync("done", new
    {
        memoriesUsed = contextBundle?.ItemCount ?? 0,
        totalLength = response.Length,
        contextStrategy,
        breakdown = contextBundle != null ? new
        {
            recent = contextBundle.Breakdown.RecentTokens,
            semantic = contextBundle.Breakdown.SemanticTokens,
            episodic = contextBundle.Breakdown.EpisodicTokens,
            fact = contextBundle.Breakdown.FactTokens
        } : null,
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

// Helper: Recall memories using Context Budget API (v0.9.0)
async Task<(ContextBundle bundle, string context)> RecallMemoriesAsync(
    string userId, string sessionId, string query)
{
    try
    {
        var request = new ContextRequest(
            UserId: userId,
            SessionId: sessionId,
            Query: query,
            Budget: new ContextBudget(TotalTokens: contextBudgetTokens)
        );

        var bundle = await contextBuilder.BuildAsync(request, contextStrategy);

        Console.WriteLine($"[MEMORY] Context built: {bundle.ItemCount} items, {bundle.TotalTokens} tokens " +
            $"(R:{bundle.Breakdown.RecentTokens}/S:{bundle.Breakdown.SemanticTokens}/E:{bundle.Breakdown.EpisodicTokens}/F:{bundle.Breakdown.FactTokens})");

        return (bundle, bundle.Content);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MEMORY] Context build error: {ex.Message}");
        return (new ContextBundle("", 0, new ContextBreakdown(0, 0, 0, 0), []), "");
    }
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
            embedding = $"{embedProvider} ({embedModel ?? "bge-large-en-v1.5"})",
            chatLlm = $"{llmProvider} ({llmModel ?? "N/A"})",
            contextStrategy,
            contextBudgetTokens
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

// SPA handling
if (app.Environment.IsDevelopment())
{
    // Development: redirect only GET requests for non-API paths to Vite dev server
    app.MapGet("{*path:nonfile}", (HttpContext context) =>
    {
        // Only redirect if not an API path
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            var spaUrl = $"http://localhost:3000{context.Request.Path}{context.Request.QueryString}";
            return Results.Redirect(spaUrl);
        }
        return Results.NotFound();
    });
    Console.WriteLine($"[SERVER] Development mode - SPA requests redirect to http://localhost:3000");
}
else
{
    // Production: serve static files from wwwroot/dist
    app.MapFallbackToFile("index.html");
}

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

/// <summary>
/// Simple wrapper around LMSupply IEmbeddingModel to implement IEmbeddingService.
/// </summary>
sealed class LMSupplyEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingModel _model;

    public LMSupplyEmbeddingService(IEmbeddingModel model)
    {
        _model = model;
    }

    public int Dimensions => _model.Dimensions;

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var result = await _model.EmbedAsync(text);
        return result;
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var results = new List<ReadOnlyMemory<float>>(textList.Count);

        foreach (var text in textList)
        {
            var embedding = await _model.EmbedAsync(text);
            results.Add(embedding);
        }

        return results;
    }
}
