using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Services;
using MemoryIndexer.Intelligence.Summarization;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Load .env file - try multiple locations
var envSearchPaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(AppContext.BaseDirectory, ".env"),
    // Look for solution root (up to 5 levels)
    FindSolutionRoot(".env")
};

foreach (var path in envSearchPaths.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)))
{
    DotNetEnv.Env.Load(path);
    break;
}

static string? FindSolutionRoot(string filename)
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var envFile = Path.Combine(dir.FullName, filename);
        if (File.Exists(envFile)) return envFile;

        // Check if this is the solution root
        if (Directory.GetFiles(dir.FullName, "*.sln").Length > 0)
        {
            return File.Exists(envFile) ? envFile : null;
        }
        dir = dir.Parent;
    }
    return null;
}

var gpuStackUrl = Environment.GetEnvironmentVariable("GPUSTACK_URL") ?? "http://localhost:11434/v1";
var gpuStackApiKey = Environment.GetEnvironmentVariable("GPUSTACK_APIKEY") ?? "";

Console.Clear();
PrintBanner();

// Build the host with Memory Indexer services
var builder = Host.CreateApplicationBuilder(args);

// Configure logging to be minimal
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Configure Memory Indexer with SQLite storage and GpuStack embeddings
builder.Services.AddMemoryIndexer(options =>
{
    // SQLite persistent storage
    options.Storage.Type = StorageType.SqliteVec;
    options.Storage.ConnectionString = "chat_memories.db";
    options.Storage.VectorDimensions = 1024;

    // GpuStack embedding (OpenAI-compatible)
    options.Embedding.Provider = EmbeddingProvider.Custom;
    options.Embedding.Endpoint = gpuStackUrl;
    options.Embedding.ApiKey = gpuStackApiKey;
    options.Embedding.Model = "bge-m3";
    options.Embedding.Dimensions = 1024;
});

var host = builder.Build();

// Get services
var memoryService = host.Services.GetRequiredService<MemoryService>();
var memoryStore = host.Services.GetRequiredService<IMemoryStore>();
var embeddingService = host.Services.GetRequiredService<IEmbeddingService>();
var summarizer = host.Services.GetRequiredService<ISummarizationService>();

// HTTP client for LLM chat
using var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri(gpuStackUrl.TrimEnd('/') + "/");
if (!string.IsNullOrEmpty(gpuStackApiKey))
{
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {gpuStackApiKey}");
}

// State
var userId = "demo-user";
var sessionId = Guid.NewGuid().ToString("N")[..8];
var chatHistory = new List<ChatMessage>();
var memorySummary = "";
var chatCancellation = new CancellationTokenSource();

Console.WriteLine($"Session ID: {sessionId}");
Console.WriteLine($"Database: chat_memories.db");
Console.WriteLine($"Embedding: {gpuStackUrl} (bge-m3)");
Console.WriteLine();

// Main menu loop
while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=== Main Menu ===");
    Console.ResetColor();
    Console.WriteLine("1. chat   - Start interactive chat");
    Console.WriteLine("2. status - View memory status");
    Console.WriteLine("3. exit   - Exit application");
    Console.WriteLine();
    Console.Write("Select option: ");

    var input = Console.ReadLine()?.Trim().ToLowerInvariant();

    switch (input)
    {
        case "1":
        case "chat":
            await RunChatModeAsync();
            break;
        case "2":
        case "status":
            await ShowStatusAsync();
            break;
        case "3":
        case "exit":
        case "quit":
            Console.WriteLine("Goodbye!");
            return;
        default:
            Console.WriteLine("Invalid option. Please try again.");
            break;
    }

    Console.WriteLine();
}

async Task RunChatModeAsync()
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("=== Chat Mode ===");
    Console.ResetColor();
    Console.WriteLine("Type your message. Type 'exit' or press Ctrl+C to return to main menu.");
    Console.WriteLine();

    // Reset cancellation token for new chat session
    chatCancellation = new CancellationTokenSource();

    // Handle Ctrl+C
    void OnCancelKeyPress(object? s, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        chatCancellation.Cancel();
    }
    Console.CancelKeyPress += OnCancelKeyPress;

    try
    {
        while (!chatCancellation.Token.IsCancellationRequested)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("You: ");
            Console.ResetColor();

            var userMessage = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                continue;
            }

            if (userMessage.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                userMessage.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                // Store user message as episodic memory
                await memoryService.StoreAsync(
                    userId,
                    $"[User said]: {userMessage}",
                    MemoryType.Episodic,
                    sessionId,
                    importance: 0.7f);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[Memory store warning: {ex.Message}]");
                Console.ResetColor();
            }

            // Recall relevant memories
            var memories = await RecallMemoriesAsync(userMessage);

            // Build context with memories
            var context = BuildContext(memories, userMessage);

            // Generate response
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("Assistant: ");
            Console.ResetColor();

            var response = await GenerateResponseAsync(context, userMessage);
            Console.WriteLine(response);
            Console.WriteLine();

            try
            {
                // Store assistant response
                await memoryService.StoreAsync(
                    userId,
                    $"[Assistant said]: {response}",
                    MemoryType.Episodic,
                    sessionId,
                    importance: 0.6f);

                // Extract and store semantic memories if important info detected
                await ExtractSemanticMemoriesAsync(userMessage, response);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[Memory store warning: {ex.Message}]");
                Console.ResetColor();
            }

            // Update rolling summary periodically
            chatHistory.Add(new ChatMessage { Role = "user", Content = userMessage });
            chatHistory.Add(new ChatMessage { Role = "assistant", Content = response });

            if (chatHistory.Count % 10 == 0)
            {
                await UpdateSummaryAsync();
            }
        }
    }
    finally
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        Console.WriteLine();
        Console.WriteLine("Returning to main menu...");
    }
}

async Task<List<MemorySearchResult>> RecallMemoriesAsync(string query)
{
    var results = new List<MemorySearchResult>();

    try
    {
        // Recent session memories (short-term)
        var recentMemories = await memoryService.RecallAsync(
            userId, query, limit: 3, sessionId: sessionId);
        results.AddRange(recentMemories);

        // Cross-session memories (long-term)
        var longTermMemories = await memoryService.RecallAsync(
            userId, query, limit: 3, sessionId: null);

        // Add long-term memories that aren't duplicates
        foreach (var mem in longTermMemories)
        {
            if (!results.Any(r => r.Memory.Id == mem.Memory.Id))
            {
                results.Add(mem);
            }
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[Memory recall warning: {ex.Message}]");
        Console.ResetColor();
    }

    return results.OrderByDescending(r => r.Score).Take(5).ToList();
}

string BuildContext(List<MemorySearchResult> memories, string currentQuery)
{
    var sb = new StringBuilder();

    if (!string.IsNullOrEmpty(memorySummary))
    {
        sb.AppendLine("## Conversation Summary");
        sb.AppendLine(memorySummary);
        sb.AppendLine();
    }

    if (memories.Count > 0)
    {
        sb.AppendLine("## Relevant Memories");
        foreach (var mem in memories)
        {
            var age = DateTime.UtcNow - mem.Memory.CreatedAt;
            var ageStr = age.TotalMinutes < 60
                ? $"{age.TotalMinutes:F0}m ago"
                : age.TotalHours < 24
                    ? $"{age.TotalHours:F0}h ago"
                    : $"{age.TotalDays:F0}d ago";

            var typeIcon = mem.Memory.Type switch
            {
                MemoryType.Episodic => "[Episode]",
                MemoryType.Semantic => "[Fact]",
                MemoryType.Procedural => "[Procedure]",
                _ => "[Memory]"
            };

            sb.AppendLine($"- {typeIcon} (score: {mem.Score:F2}, {ageStr}): {mem.Memory.Content}");
        }
        sb.AppendLine();
    }

    return sb.ToString();
}

async Task<string> GenerateResponseAsync(string context, string userMessage)
{
    var systemPrompt = $"""
You are a helpful AI assistant with persistent memory capabilities.
You remember previous conversations and facts about the user.

{context}

Based on the memories above (if any), respond naturally to the user's message.
If you don't have relevant memories, just respond helpfully.
Keep responses concise but friendly.
""";

    var request = new ChatRequest
    {
        Model = "Qwen3-8B",
        Messages =
        [
            new ChatMessage { Role = "system", Content = systemPrompt },
            new ChatMessage { Role = "user", Content = userMessage }
        ],
        MaxTokens = 500,
        Temperature = 0.7f
    };

    try
    {
        var response = await httpClient.PostAsJsonAsync("chat/completions", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>();
        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? "I'm sorry, I couldn't generate a response.";
    }
    catch (Exception ex)
    {
        return $"[Error generating response: {ex.Message}]";
    }
}

async Task ExtractSemanticMemoriesAsync(string userMessage, string response)
{
    // Simple heuristic: if user shares facts about themselves, store as semantic memory
    var factPatterns = new[]
    {
        "my name is", "i am", "i work", "i like", "i love", "i have", "i live",
        "my favorite", "i prefer", "i usually", "i always", "i never"
    };

    var lowerMessage = userMessage.ToLowerInvariant();
    if (factPatterns.Any(p => lowerMessage.Contains(p)))
    {
        try
        {
            await memoryService.StoreAsync(
                userId,
                $"[User fact]: {userMessage}",
                MemoryType.Semantic,
                null, // No session - long-term memory
                importance: 0.9f);
        }
        catch
        {
            // Ignore storage errors
        }
    }
}

async Task UpdateSummaryAsync()
{
    if (chatHistory.Count < 4) return;

    try
    {
        // Create memory units from chat history for summarization
        var recentMessages = chatHistory.TakeLast(10).ToList();
        var content = string.Join("\n", recentMessages.Select(m => $"{m.Role}: {m.Content}"));

        var memoryUnits = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = userId
            }
        };

        var summary = await summarizer.SummarizeAsync(memoryUnits, new SummarizationOptions
        {
            TargetCompressionRatio = 0.3f,
            Style = SummaryStyle.Extractive
        });

        memorySummary = summary.Content;
    }
    catch
    {
        // Ignore summarization errors
    }
}

async Task ShowStatusAsync()
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Blue;
    Console.WriteLine("=== Memory Status ===");
    Console.ResetColor();
    Console.WriteLine();

    try
    {
        // Get all memories
        var allMemories = await memoryStore.GetAllAsync(userId);

        // Statistics
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("## Overview");
        Console.ResetColor();
        Console.WriteLine($"Total memories: {allMemories.Count}");
        Console.WriteLine($"Current session: {sessionId}");
        Console.WriteLine($"Database: chat_memories.db");
        Console.WriteLine();

        // By type
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("## By Memory Type");
        Console.ResetColor();
        var byType = allMemories.GroupBy(m => m.Type).OrderByDescending(g => g.Count());
        foreach (var group in byType)
        {
            var icon = group.Key switch
            {
                MemoryType.Episodic => "[Episode]",
                MemoryType.Semantic => "[Fact]",
                MemoryType.Procedural => "[Procedure]",
                _ => "[Other]"
            };
            Console.WriteLine($"  {icon} {group.Key}: {group.Count()}");
        }
        Console.WriteLine();

        // By session
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("## By Session");
        Console.ResetColor();
        var bySession = allMemories.GroupBy(m => m.SessionId ?? "long-term").OrderByDescending(g => g.Count());
        foreach (var group in bySession.Take(5))
        {
            var label = group.Key == sessionId ? $"{group.Key} (current)" : group.Key;
            Console.WriteLine($"  {label}: {group.Count()} memories");
        }
        Console.WriteLine();

        // Recent memories
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("## Recent Memories (last 5)");
        Console.ResetColor();
        var recent = allMemories.OrderByDescending(m => m.CreatedAt).Take(5);
        foreach (var mem in recent)
        {
            var age = DateTime.UtcNow - mem.CreatedAt;
            var ageStr = age.TotalMinutes < 60
                ? $"{age.TotalMinutes:F0}m ago"
                : age.TotalHours < 24
                    ? $"{age.TotalHours:F0}h ago"
                    : $"{age.TotalDays:F0}d ago";

            var preview = mem.Content.Length > 60
                ? mem.Content[..60] + "..."
                : mem.Content;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [{ageStr}] ");
            Console.ResetColor();
            Console.WriteLine(preview);
        }
        Console.WriteLine();

        // Important memories
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("## High Importance Memories (score > 0.8)");
        Console.ResetColor();
        var important = allMemories.Where(m => m.ImportanceScore > 0.8f).OrderByDescending(m => m.ImportanceScore).Take(5);
        foreach (var mem in important)
        {
            var preview = mem.Content.Length > 60
                ? mem.Content[..60] + "..."
                : mem.Content;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  [{mem.ImportanceScore:F2}] ");
            Console.ResetColor();
            Console.WriteLine(preview);
        }
        if (!important.Any())
        {
            Console.WriteLine("  (none)");
        }
        Console.WriteLine();

        // Storage info
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("## Storage Details");
        Console.ResetColor();
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "chat_memories.db");
        if (File.Exists(dbPath))
        {
            var fileInfo = new FileInfo(dbPath);
            Console.WriteLine($"  Database size: {fileInfo.Length / 1024.0:F1} KB");
            Console.WriteLine($"  Last modified: {fileInfo.LastWriteTime}");
        }
        Console.WriteLine($"  Vector dimensions: 1024 (bge-m3)");
        Console.WriteLine();

        // Current session summary
        if (!string.IsNullOrEmpty(memorySummary))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("## Current Session Summary");
            Console.ResetColor();
            Console.WriteLine($"  {memorySummary}");
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error loading status: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine("Press any key to return to main menu...");
    Console.ReadKey(true);
}

void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"
  __  __                                    _____ _           _
 |  \/  | ___ _ __ ___   ___  _ __ _   _   / ____| |__   __ _| |_
 | |\/| |/ _ \ '_ ` _ \ / _ \| '__| | | | | |    | '_ \ / _` | __|
 | |  | |  __/ | | | | | (_) | |  | |_| | | |____| | | | (_| | |_
 |_|  |_|\___|_| |_| |_|\___/|_|   \__, |  \_____|_| |_|\__,_|\__|
                                    __/ |
                                   |___/   Memory-Indexer Demo
");
    Console.ResetColor();
}

// Chat API models
public class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 500;

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public class ChatResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }
}

public class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }
}
