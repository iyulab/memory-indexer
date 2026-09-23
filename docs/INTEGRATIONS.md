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
using MemoryIndexer;
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
    options.Embedding.Dimensions = 1536;  // Match your embedding model
}).WithSqliteVec();

var kernel = builder.Build();
```

### Memory-Enhanced Chat Function

```csharp
using MemoryIndexer.Interfaces;
using Microsoft.SemanticKernel;

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
        await _memory.RememberAsync(userId, message, role: "user");

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
        await _memory.RememberAsync(userId, response.Content!, role: "assistant");

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
        await _memory.RememberAsync(userId, input, role: "user");
        await _memory.RememberAsync(userId, result, role: "assistant");

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
        await _memory.RememberAsync(_userId, userMessage.Content!, role: "user");
        await _memory.RememberAsync(_userId, assistantMessage.Content!, role: "assistant");
    }
}
```

### Multi-Agent System with Shared Memory

```csharp
public class MemoryMultiAgentSystem
{
    private readonly IMemoryService _memory;
    private readonly string _userId;

    public async Task RunCollaborativeTaskAsync(string task)
    {
        // Create agents with shared memory
        var planner = new MemoryAgent(_memory, _userId, apiKey);
        var executor = new MemoryAgent(_memory, _userId, apiKey);
        var reviewer = new MemoryAgent(_memory, _userId, apiKey);

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
        await _memory.RememberAsync(userId, content, role: role);
    }
}
```

### Custom Chat Loop with Memory

```csharp
public class CustomMemoryChatLoop
{
    private readonly IMemoryProvider _memory;
    private readonly HttpClient _httpClient;

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
```

### Middleware Pattern

```csharp
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
            await _memory.RememberAsync(userId!, message, role: "user");

            // Capture response
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context);

            // Store response
            responseBody.Seek(0, SeekOrigin.Begin);
            var response = await new StreamReader(responseBody).ReadToEndAsync();

            await _memory.RememberAsync(userId!, response, role: "assistant");

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

// Register in Startup.cs
app.UseMiddleware<MemoryMiddleware>();
```

---

## Best Practices

### 1. Separation of Concerns

```csharp
// Good: Separate memory management from business logic
public class ChatService
{
    private readonly IMemoryProvider _memory;
    private readonly ILlmService _llm;

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
        var line = $"[{memory.Type}] {memory.Content}\n";
        if (currentLength + line.Length > tokenBudget) break;

        builder.AppendLine(line);
        currentLength += line.Length;
    }

    return builder.ToString();
}
```

### 3. Error Handling and Fallback

```csharp
public async Task<string> ChatWithFallbackAsync(string userId, string message)
{
    try
    {
        // Try with memory context
        var context = await _memory.GetContextAsync(userId, message);
        return await _llm.GenerateAsync(context, message);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Memory retrieval failed, using stateless mode");

        // Fallback to stateless chat
        return await _llm.GenerateAsync(string.Empty, message);
    }
}
```

### 4. Async Batch Processing

```csharp
// Good: Process memory storage asynchronously
public class BackgroundMemoryProcessor : BackgroundService
{
    private readonly Channel<MemoryItem> _channel;
    private readonly IMemoryService _memory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _memory.RememberAsync(
                    item.UserId,
                    item.Content,
                    role: item.Role,
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex)
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
public class CachedMemoryProvider : IMemoryProvider
{
    private readonly IMemoryCache _cache;
    private readonly IMemoryService _memory;

    public async Task<string> GetContextAsync(string userId, string query)
    {
        var cacheKey = $"memory:{userId}:{query.GetHashCode()}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var memories = await _memory.RecallAsync(userId, sessionId: null, query);
            return BuildContext(memories.AllMemories());
        });
    }
}
```

### Parallel Graph Expansion

Graph traversal starts from a memory, so recall first and then expand every hit in parallel
through `IMemoryGraphService`:

```csharp
public async Task<string> GetMultiSourceContextAsync(string userId, string query)
{
    var recalled = await _memory.RecallAsync(userId, sessionId: null, query, limit: 5);
    var hits = recalled.AllMemories().ToList();

    var expansions = await Task.WhenAll(hits.Select(m =>
        _graph.FindRelatedMemoriesAsync(m.Id, maxHops: 2, topK: 3)));

    var related = expansions.SelectMany(r => r).Select(r => r.Memory);
    return CombineContext(hits, related);
}
```

---

## Next Steps

- **Architecture Overview**: [Architecture](ARCHITECTURE.md)
- **Common Patterns**: [Patterns](PATTERNS.md)
- **Production Deployment**: [Kubernetes Guide](../deploy/kubernetes/README.md)
- **Best Practices**: [Best Practices](BEST_PRACTICES.md)
