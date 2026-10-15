# LLM Framework Integrations

Integrate Memory Indexer with popular LLM frameworks for enhanced conversational AI.

## Table of Contents

- [Semantic Kernel](#semantic-kernel-microsoft)
- [LangChain for .NET](#langchain-for-net)
- [AutoGen](#autogen-microsoft)
- [Custom Frameworks](#custom-frameworks)

---

## Semantic Kernel (Microsoft)

### Installation

```bash
dotnet add package Microsoft.SemanticKernel
dotnet add package MemoryIndexer.Sdk
```

### Basic Integration

```csharp
using Microsoft.SemanticKernel;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;

var builder = Kernel.CreateBuilder();

// Add OpenAI chat completion
builder.AddOpenAIChatCompletion(
    modelId: "gpt-4",
    apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);

// Register your embedding service (e.g., OpenAI, Azure, local ONNX)
builder.Services.AddSingleton<IEmbeddingService>(myEmbeddingService);

// Add Memory Indexer with SQLite storage
builder.Services.AddMemoryIndexer(options =>
{
    options.Storage.ConnectionString = "memories.db";
}).WithSqliteVec();

var kernel = builder.Build();
```

### Memory-Enhanced Chat Function

```csharp
using System.ComponentModel;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

public class MemoryChatPlugin
{
    private readonly IMemoryService _memory;
    private readonly Kernel _kernel;

    public MemoryChatPlugin(IMemoryService memory, Kernel kernel)
    {
        _memory = memory;
        _kernel = kernel;
    }

    [KernelFunction("chat_with_memory")]
    [Description("Chat with persistent memory across sessions")]
    public async Task<string> ChatAsync(
        [Description("User ID")] string userId,
        [Description("User message")] string message)
    {
        // 1. Store the user message (the memory type is classified automatically)
        await _memory.RememberAsync(userId, null, message, role: "user");

        // 2. Recall relevant memories across sessions
        var context = await _memory.RecallAsync(userId, sessionId: null, message, limit: 10);

        // 3. Build prompt with memory context
        var systemPrompt = BuildSystemPrompt(context.AllMemories());
        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(message);

        // 4. Get LLM response
        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatCompletion.GetChatMessageContentAsync(
            chatHistory,
            kernel: _kernel);

        // 5. Store assistant response
        await _memory.RememberAsync(userId, null, response.Content!, role: "assistant");

        return response.Content!;
    }

    private string BuildSystemPrompt(IEnumerable<MemoryUnit> memories)
    {
        var contextLines = memories.Select(m =>
            $"[{m.Type}] {m.Content} (Importance: {m.ImportanceScore:F2})");

        return $"""
            You are a helpful AI assistant with access to conversation history.

            ## Relevant Context:
            {string.Join("\n", contextLines)}

            Use this context to provide personalized and contextually aware responses.
            """;
    }
}
```

### Usage Example

```csharp
var plugin = kernel.ImportPluginFromObject(new MemoryChatPlugin(memory, kernel));

var result = await kernel.InvokeAsync(
    plugin["chat_with_memory"],
    new KernelArguments
    {
        ["userId"] = "user-123",
        ["message"] = "What did we discuss about TypeScript last week?"
    });

Console.WriteLine(result);
```

---

## LangChain for .NET

### Installation

```bash
dotnet add package LangChain
dotnet add package LangChain.Providers.OpenAI
dotnet add package MemoryIndexer.Sdk
```

### Memory-Backed Conversation Chain

```csharp
using LangChain.Chains;
using LangChain.Providers;
using LangChain.Providers.OpenAI;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

public class MemoryConversationChain
{
    private readonly IMemoryService _memory;
    private readonly OpenAiProvider _provider;

    public MemoryConversationChain(
        IMemoryService memory,
        string apiKey)
    {
        _memory = memory;
        _provider = new OpenAiProvider(apiKey);
    }

    public async Task<string> RunAsync(string userId, string input)
    {
        // 1. Retrieve memory context
        var memories = await _memory.RecallAsync(userId, sessionId: null, input, limit: 10);

        // 2. Build context string
        var context = string.Join("\n\n", memories.AllMemories().Select(m =>
            $"[{m.CreatedAt:yyyy-MM-dd}] {m.Content}"));

        // 3. Create LangChain prompt
        var model = _provider.CreateChatModel("gpt-4");

        var chain = Chain
            .Set(input, "input")
            .Set(context, "context")
            .Template("""
                Context from previous conversations:
                {context}

                Current question: {input}

                Please provide a response that takes into account the conversation history.
                """)
            .LLM(model);

        // 4. Execute chain
        var result = await chain.RunAsync("text");

        // 5. Store conversation turn
        await _memory.RememberAsync(userId, null, input, role: "user");
        await _memory.RememberAsync(userId, null, result, role: "assistant");

        return result;
    }
}
```

### Custom Memory Retriever

```csharp
using LangChain.Memory;
using MemoryIndexer.Interfaces;

public class MemoryIndexerRetriever : IBaseRetriever
{
    private readonly IMemoryPrimitives _memory;
    private readonly string _userId;

    public MemoryIndexerRetriever(IMemoryPrimitives memory, string userId)
    {
        _memory = memory;
        _userId = userId;
    }

    public async Task<IEnumerable<string>> GetRelevantDocumentsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var results = await _memory.RetrieveAsync(
            new RetrieveRequest { UserId = _userId, Query = query, Limit = 10 },
            cancellationToken);

        return results.Select(r => r.Memory.Content);
    }
}

// Usage
var retriever = new MemoryIndexerRetriever(memoryPrimitives, userId);
var retrievalChain = new RetrievalQAChain
{
    Retriever = retriever,
    LLM = provider.CreateChatModel("gpt-4")
};

var answer = await retrievalChain.RunAsync("What are my project goals?");
```

---

## AutoGen (Microsoft)

### Installation

```bash
dotnet add package AutoGen.Core
dotnet add package AutoGen.OpenAI
dotnet add package MemoryIndexer.Sdk
```

### Memory-Enhanced Agent

```csharp
using AutoGen.Core;
using AutoGen.OpenAI;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

public class MemoryAgent : IAgent
{
    private readonly IMemoryService _memory;
    private readonly OpenAIChatAgent _innerAgent;
    private readonly string _userId;

    public string Name => "MemoryAssistant";

    public MemoryAgent(
        IMemoryService memory,
        string userId,
        string openAiApiKey)
    {
        _memory = memory;
        _userId = userId;

        _innerAgent = new OpenAIChatAgent(
            name: "Assistant",
            modelName: "gpt-4",
            apiKey: openAiApiKey);
    }

    public async Task<IMessage> GenerateReplyAsync(
        IEnumerable<IMessage> messages,
        GenerateReplyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = messages.Last();

        // 1. Retrieve memory context
        var context = await _memory.RecallAsync(
            _userId,
            sessionId: null,
            lastMessage.Content!,
            limit: 10);

        // 2. Augment message with memory
        var augmentedMessages = new List<IMessage>
        {
            new TextMessage(
                Role.System,
                BuildMemoryContext(context.AllMemories()),
                from: "System")
        };
        augmentedMessages.AddRange(messages);

        // 3. Generate response
        var response = await _innerAgent.GenerateReplyAsync(
            augmentedMessages,
            options,
            cancellationToken);

        // 4. Store conversation
        await StoreConversationTurnAsync(lastMessage, response);

        return response;
    }

    private string BuildMemoryContext(IEnumerable<MemoryUnit> memories)
    {
        return $"""
            ## Memory Context:
            {string.Join("\n", memories.Select(m =>
                $"- [{m.Type}] {m.Content}"))}
            """;
    }

    private async Task StoreConversationTurnAsync(
        IMessage userMessage,
        IMessage assistantMessage)
    {
        await _memory.RememberAsync(_userId, null, userMessage.Content!, role: "user");
        await _memory.RememberAsync(_userId, null, assistantMessage.Content!, role: "assistant");
    }
}
```

### Multi-Agent System with Shared Memory

```csharp
public class MemoryMultiAgentSystem
{
    private readonly IMemoryService _memory;
    private readonly string _userId;
    private readonly string _apiKey;

    public MemoryMultiAgentSystem(IMemoryService memory, string userId, string apiKey) =>
        (_memory, _userId, _apiKey) = (memory, userId, apiKey);

    public async Task RunCollaborativeTaskAsync(string task)
    {
        // Create agents with shared memory
        var planner = new MemoryAgent(_memory, _userId, _apiKey);
        var executor = new MemoryAgent(_memory, _userId, _apiKey);
        var reviewer = new MemoryAgent(_memory, _userId, _apiKey);

        // Sequential conversation
        var planMessage = await planner.GenerateReplyAsync(
            new[] { new TextMessage(Role.User, $"Plan: {task}") });

        var executeMessage = await executor.GenerateReplyAsync(
            new[] { planMessage });

        var reviewMessage = await reviewer.GenerateReplyAsync(
            new[] { executeMessage });

        // All agents share memory through the same IMemoryService
        // Each agent sees the full conversation history
    }
}
```

---

## Custom Frameworks

### Custom Memory Provider Interface

```csharp
public interface IMemoryProvider
{
    Task<string> GetContextAsync(string userId, string query);
    Task StoreInteractionAsync(string userId, string role, string content);
}

public class MemoryIndexerProvider : IMemoryProvider
{
    private readonly IMemoryService _memory;

    public MemoryIndexerProvider(IMemoryService memory)
    {
        _memory = memory;
    }

    public async Task<string> GetContextAsync(string userId, string query)
    {
        var memories = await _memory.RecallAsync(userId, sessionId: null, query, limit: 10);

        return string.Join("\n\n", memories.AllMemories().Select(m =>
            $"[{m.Type}] {m.Content}"));
    }

    public async Task StoreInteractionAsync(
        string userId,
        string role,
        string content)
    {
        await _memory.RememberAsync(userId, null, content, role: role);
    }
}
```

### Custom Chat Loop with Memory

```csharp
using System.Net.Http.Json;

public class CustomMemoryChatLoop
{
    private readonly IMemoryProvider _memory;
    private readonly HttpClient _httpClient;

    public CustomMemoryChatLoop(IMemoryProvider memory, HttpClient httpClient) =>
        (_memory, _httpClient) = (memory, httpClient);

    public async Task<string> ChatAsync(string userId, string message)
    {
        // 1. Get memory context
        var context = await _memory.GetContextAsync(userId, message);

        // 2. Call your LLM API
        var response = await CallLlmApiAsync(context, message);

        // 3. Store interaction
        await _memory.StoreInteractionAsync(userId, "user", message);
        await _memory.StoreInteractionAsync(userId, "assistant", response);

        return response;
    }

    private async Task<string> CallLlmApiAsync(string context, string message)
    {
        // Your custom LLM API integration
        var request = new
        {
            messages = new[]
            {
                new { role = "system", content = $"Context:\n{context}" },
                new { role = "user", content = message }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            "https://api.your-llm.com/chat",
            request);

        var result = await response.Content.ReadFromJsonAsync<LlmResponse>();
        return result!.Message;
    }
}

// The shape of your LLM API's reply
public sealed record LlmResponse(string Message);
```

### Middleware Pattern

```csharp
using Microsoft.AspNetCore.Http;

public class MemoryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryService _memory;

    public MemoryMiddleware(
        RequestDelegate next,
        IMemoryService memory)
    {
        _next = next;
        _memory = memory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/chat"))
        {
            var userId = context.User.FindFirst("user_id")?.Value;
            var message = await ReadMessageAsync(context.Request);

            // Store request
            await _memory.RememberAsync(userId!, null, message, role: "user");

            // Capture response
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context);

            // Store response
            responseBody.Seek(0, SeekOrigin.Begin);
            var response = await new StreamReader(responseBody).ReadToEndAsync();

            await _memory.RememberAsync(userId!, null, response, role: "assistant");

            // Copy response back
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        else
        {
            await _next(context);
        }
    }

    private async Task<string> ReadMessageAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return body;
    }
}

// Register in Program.cs
app.UseMiddleware<MemoryMiddleware>();
```

---

## Best Practices

### 1. Separation of Concerns

```csharp
// Your model client, behind an interface of your own
public interface ILlmService
{
    Task<string> GenerateAsync(string context, string message);
}

// Good: Separate memory management from business logic
public class ChatService
{
    private readonly IMemoryProvider _memory;
    private readonly ILlmService _llm;

    public ChatService(IMemoryProvider memory, ILlmService llm) => (_memory, _llm) = (memory, llm);

    public async Task<string> ProcessMessageAsync(string userId, string message)
    {
        var context = await _memory.GetContextAsync(userId, message);
        var response = await _llm.GenerateAsync(context, message);
        await _memory.StoreInteractionAsync(userId, "user", message);
        await _memory.StoreInteractionAsync(userId, "assistant", response);
        return response;
    }
}
```

### 2. Token Budget Management

```csharp
using System.Text;

public class BudgetedMemoryProvider
{
    private readonly IMemoryService _memory;

    public BudgetedMemoryProvider(IMemoryService memory) => _memory = memory;

    // Limit context size to fit model's token limit
    public async Task<string> GetContextAsync(string userId, string query, int maxTokens = 1000)
    {
        var memories = await _memory.RecallAsync(userId, sessionId: null, query, limit: 50);

        // Estimate tokens (rough approximation: 1 token ≈ 4 characters)
        var tokenBudget = maxTokens * 4;
        var builder = new StringBuilder();
        var currentLength = 0;

        foreach (var memory in memories.AllMemories())
        {
            var line = $"[{memory.Type}] {memory.Content}";
            if (currentLength + line.Length > tokenBudget) break;

            builder.AppendLine(line);
            currentLength += line.Length + 1;
        }

        return builder.ToString();
    }
}
```

For a budget measured in tokens rather than characters, `IContextBuilder.BuildAsync` with a `ContextBudget`
assembles the context for you (see [Architecture](ARCHITECTURE.md#context-budget-api-v090)).

### 3. Error Handling and Fallback

```csharp
public class ResilientChatService
{
    private readonly IMemoryProvider _memory;
    private readonly ILlmService _llm;
    private readonly ILogger<ResilientChatService> _logger;

    public ResilientChatService(IMemoryProvider memory, ILlmService llm, ILogger<ResilientChatService> logger) =>
        (_memory, _llm, _logger) = (memory, llm, logger);

    public async Task<string> ChatWithFallbackAsync(string userId, string message)
    {
        string context;
        try
        {
            // Try with memory context
            context = await _memory.GetContextAsync(userId, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Memory retrieval failed, using stateless mode");

            // Fallback to stateless chat
            context = string.Empty;
        }

        return await _llm.GenerateAsync(context, message);
    }
}
```

### 4. Async Batch Processing

```csharp
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

// Good: Process memory storage asynchronously
public class BackgroundMemoryProcessor : BackgroundService
{
    private readonly Channel<MemoryItem> _channel = Channel.CreateUnbounded<MemoryItem>();
    private readonly IMemoryService _memory;
    private readonly ILogger<BackgroundMemoryProcessor> _logger;

    public BackgroundMemoryProcessor(IMemoryService memory, ILogger<BackgroundMemoryProcessor> logger) =>
        (_memory, _logger) = (memory, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _memory.RememberAsync(
                    item.UserId,
                    sessionId: null,
                    item.Content,
                    role: item.Role,
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to process memory");
            }
        }
    }

    public void Enqueue(MemoryItem item)
    {
        _channel.Writer.TryWrite(item);
    }
}

public sealed record MemoryItem(string UserId, string Content, string? Role);
```

---

## Performance Considerations

### Caching Frequently Retrieved Context

```csharp
using Microsoft.Extensions.Caching.Memory;

public class CachedMemoryProvider : IMemoryProvider
{
    private readonly IMemoryCache _cache;
    private readonly IMemoryService _memory;

    public CachedMemoryProvider(IMemoryCache cache, IMemoryService memory) => (_cache, _memory) = (cache, memory);

    public async Task<string> GetContextAsync(string userId, string query)
    {
        var cacheKey = $"memory:{userId}:{query}";

        var context = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var memories = await _memory.RecallAsync(userId, sessionId: null, query);
            return string.Join("\n\n", memories.AllMemories().Select(m => $"[{m.Type}] {m.Content}"));
        });

        return context ?? string.Empty;
    }

    public Task StoreInteractionAsync(string userId, string role, string content) =>
        _memory.RememberAsync(userId, null, content, role: role);
}
```

### Parallel Graph Expansion

Graph traversal starts from a memory, so recall first and then expand every hit in parallel
through `IMemoryGraphService`:

```csharp
public class GraphExpandedContext
{
    private readonly IMemoryService _memory;
    private readonly IMemoryGraphService _graph;

    public GraphExpandedContext(IMemoryService memory, IMemoryGraphService graph) => (_memory, _graph) = (memory, graph);

    public async Task<string> GetMultiSourceContextAsync(string userId, string query)
    {
        var recalled = await _memory.RecallAsync(userId, sessionId: null, query, limit: 5);
        var hits = recalled.AllMemories().ToList();

        var expansions = await Task.WhenAll(hits.Select(m =>
            _graph.FindRelatedMemoriesAsync(userId, m.Id, maxHops: 2, topK: 3)));

        var related = expansions.SelectMany(r => r).Select(r => r.Memory);
        return string.Join("\n\n", hits.Concat(related).DistinctBy(m => m.Id).Select(m => m.Content));
    }
}
```

---

## Next Steps

- **Architecture Overview**: [Architecture](ARCHITECTURE.md)
- **Guides**: [Guides](GUIDES.md)
