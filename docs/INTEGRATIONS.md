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

// Add Memory Indexer
builder.Services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Embedding.Provider = EmbeddingProvider.Ollama;
    options.Embedding.Model = "bge-m3";
});

var kernel = builder.Build();
```

### Memory-Enhanced Chat Function

```csharp
using MemoryIndexer.Interfaces;
using Microsoft.SemanticKernel;

public class MemoryChatPlugin
{
    private readonly IVirtualContextManager _vcm;
    private readonly Kernel _kernel;

    public MemoryChatPlugin(IVirtualContextManager vcm, Kernel kernel)
    {
        _vcm = vcm;
        _kernel = kernel;
    }

    [KernelFunction("chat_with_memory")]
    [Description("Chat with persistent memory across sessions")]
    public async Task<string> ChatAsync(
        [Description("User ID")] string userId,
        [Description("User message")] string message)
    {
        // 1. Store user message in Recently Buffer
        await _vcm.AddToRecentlyAsync(userId, message, new()
        {
            ["role"] = "user",
            ["timestamp"] = DateTime.UtcNow
        });

        // 2. Retrieve relevant context from all tiers
        var context = await _vcm.RetrieveHybridAsync(userId, message, limit: 10);

        // 3. Build prompt with memory context
        var systemPrompt = BuildSystemPrompt(context);
        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(message);

        // 4. Get LLM response
        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatCompletion.GetChatMessageContentAsync(
            chatHistory,
            kernel: _kernel);

        // 5. Store assistant response
        await _vcm.AddToRecentlyAsync(userId, response.Content!, new()
        {
            ["role"] = "assistant",
            ["timestamp"] = DateTime.UtcNow
        });

        return response.Content!;
    }

    private string BuildSystemPrompt(IEnumerable<MemoryUnit> memories)
    {
        var contextLines = memories.Select(m =>
            $"[{m.Type}] {m.Content} (Relevance: {m.RelevanceScore:F2})");

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
var plugin = kernel.ImportPluginFromObject(new MemoryChatPlugin(vcm, kernel));

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
    private readonly IVirtualContextManager _vcm;
    private readonly OpenAiProvider _provider;

    public MemoryConversationChain(
        IVirtualContextManager vcm,
        string apiKey)
    {
        _vcm = vcm;
        _provider = new OpenAiProvider(apiKey);
    }

    public async Task<string> RunAsync(string userId, string input)
    {
        // 1. Retrieve memory context
        var memories = await _vcm.RetrieveHybridAsync(userId, input, limit: 10);

        // 2. Build context string
        var context = string.Join("\n\n", memories.Select(m =>
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
        await _vcm.AddToRecentlyAsync(userId, input, new()
        {
            ["role"] = "user"
        });

        await _vcm.AddToRecentlyAsync(userId, result, new()
        {
            ["role"] = "assistant"
        });

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
        var memories = await _memory.RetrieveAsync(
            _userId,
            query,
            limit: 10);

        return memories.Select(m => m.Content);
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
    private readonly IVirtualContextManager _vcm;
    private readonly OpenAIChatAgent _innerAgent;
    private readonly string _userId;

    public string Name => "MemoryAssistant";

    public MemoryAgent(
        IVirtualContextManager vcm,
        string userId,
        string openAiApiKey)
    {
        _vcm = vcm;
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
        var context = await _vcm.RetrieveHybridAsync(
            _userId,
            lastMessage.Content!,
            limit: 10);

        // 2. Augment message with memory
        var augmentedMessages = new List<IMessage>
        {
            new TextMessage(
                Role.System,
                BuildMemoryContext(context),
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
        await _vcm.AddToRecentlyAsync(_userId, userMessage.Content!, new()
        {
            ["role"] = "user"
        });

        await _vcm.AddToRecentlyAsync(_userId, assistantMessage.Content!, new()
        {
            ["role"] = "assistant"
        });
    }
}
```

### Multi-Agent System with Shared Memory

```csharp
public class MemoryMultiAgentSystem
{
    private readonly IVirtualContextManager _vcm;
    private readonly string _userId;

    public async Task RunCollaborativeTaskAsync(string task)
    {
        // Create agents with shared memory
        var planner = new MemoryAgent(_vcm, _userId, apiKey);
        var executor = new MemoryAgent(_vcm, _userId, apiKey);
        var reviewer = new MemoryAgent(_vcm, _userId, apiKey);

        // Sequential conversation
        var planMessage = await planner.GenerateReplyAsync(
            new[] { new TextMessage(Role.User, $"Plan: {task}") });

        var executeMessage = await executor.GenerateReplyAsync(
            new[] { planMessage });

        var reviewMessage = await reviewer.GenerateReplyAsync(
            new[] { executeMessage });

        // All agents share memory via VCM
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
    private readonly IVirtualContextManager _vcm;

    public MemoryIndexerProvider(IVirtualContextManager vcm)
    {
        _vcm = vcm;
    }

    public async Task<string> GetContextAsync(string userId, string query)
    {
        var memories = await _vcm.RetrieveHybridAsync(userId, query, limit: 10);

        return string.Join("\n\n", memories.Select(m =>
            $"[{m.Type}] {m.Content}"));
    }

    public async Task StoreInteractionAsync(
        string userId,
        string role,
        string content)
    {
        await _vcm.AddToRecentlyAsync(userId, content, new()
        {
            ["role"] = role,
            ["timestamp"] = DateTime.UtcNow
        });
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
    private readonly IVirtualContextManager _vcm;

    public MemoryMiddleware(
        RequestDelegate next,
        IVirtualContextManager vcm)
    {
        _next = next;
        _vcm = vcm;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/chat"))
        {
            var userId = context.User.FindFirst("user_id")?.Value;
            var message = await ReadMessageAsync(context.Request);

            // Store request
            await _vcm.AddToRecentlyAsync(userId!, message, new()
            {
                ["role"] = "user",
                ["endpoint"] = context.Request.Path
            });

            // Capture response
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context);

            // Store response
            responseBody.Seek(0, SeekOrigin.Begin);
            var response = await new StreamReader(responseBody).ReadToEndAsync();

            await _vcm.AddToRecentlyAsync(userId!, response, new()
            {
                ["role"] = "assistant",
                ["endpoint"] = context.Request.Path
            });

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
        await _memory.StoreAsync(userId, message, response);
        return response;
    }
}
```

### 2. Token Budget Management

```csharp
// Limit context size to fit model's token limit
public async Task<string> GetContextAsync(string userId, string query, int maxTokens = 1000)
{
    var memories = await _vcm.RetrieveHybridAsync(userId, query, limit: 50);

    // Estimate tokens (rough approximation: 1 token ≈ 4 characters)
    var tokenBudget = maxTokens * 4;
    var builder = new StringBuilder();
    var currentLength = 0;

    foreach (var memory in memories)
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
    private readonly IVirtualContextManager _vcm;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _vcm.AddToRecentlyAsync(
                    item.UserId,
                    item.Content,
                    item.Metadata);
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
```

---

## Performance Considerations

### Caching Frequently Retrieved Context

```csharp
public class CachedMemoryProvider : IMemoryProvider
{
    private readonly IMemoryCache _cache;
    private readonly IVirtualContextManager _vcm;

    public async Task<string> GetContextAsync(string userId, string query)
    {
        var cacheKey = $"memory:{userId}:{query.GetHashCode()}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var memories = await _vcm.RetrieveHybridAsync(userId, query);
            return BuildContext(memories);
        });
    }
}
```

### Parallel Context Retrieval

```csharp
public async Task<string> GetMultiSourceContextAsync(string userId, string query)
{
    var tasks = new[]
    {
        _vcm.RetrieveHybridAsync(userId, query, limit: 5),
        _profile.RecallFactsAsync(userId, query),
        _graph.GetRelatedEntitiesAsync(userId, ExtractEntities(query))
    };

    await Task.WhenAll(tasks);

    return CombineContext(tasks[0].Result, tasks[1].Result, tasks[2].Result);
}
```

---

## Next Steps

- **Architecture Overview**: [Architecture](ARCHITECTURE.md)
- **Common Patterns**: [Patterns](PATTERNS.md)
- **Production Deployment**: [Kubernetes Guide](../deploy/kubernetes/README.md)
- **Best Practices**: [Best Practices](BEST_PRACTICES.md)
