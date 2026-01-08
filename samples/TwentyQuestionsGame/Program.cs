using DotNetEnv;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using TwentyQuestionsGame.Agents;
using TwentyQuestionsGame.Benchmark;
using TwentyQuestionsGame.Game;
using TwentyQuestionsGame.LLM;
using TwentyQuestionsGame.ToolCall;

// ═══════════════════════════════════════════════════════════════════════════════
//  Twenty Questions Game - Memory Indexer Demo
//  AI vs AI: Alpha generates secret, Beta guesses through memory-only context
// ═══════════════════════════════════════════════════════════════════════════════

// Parse CLI arguments
var benchmarkMode = args.Contains("--benchmark") || args.Contains("-b");
var iterations = GetIntArg(args, "--iterations", "-n") ?? 1;
var outputPath = GetStringArg(args, "--output", "-o");

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return;
}

Console.WriteLine("""
    ╔══════════════════════════════════════════════════════════════╗
    ║          Twenty Questions Game - Memory Demo                  ║
    ║          AI vs AI with LLM-generated secrets                 ║
    ╚══════════════════════════════════════════════════════════════╝
    """);

// 1. Load environment
Env.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));
Env.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));

var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY not found");
var llmModel = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gpt-4o";
var embeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? "text-embedding-3-small";

Console.WriteLine($"[CONFIG] LLM: {llmModel}");
Console.WriteLine($"[CONFIG] Embedding: {embeddingModel}");
if (benchmarkMode)
{
    Console.WriteLine($"[CONFIG] Mode: BENCHMARK ({iterations} iteration(s))");
    if (!string.IsNullOrEmpty(outputPath))
        Console.WriteLine($"[CONFIG] Output: {outputPath}");
}

// 2. Setup DI
var services = new ServiceCollection();

var configuration = new ConfigurationBuilder().Build();
services.AddSingleton<IConfiguration>(configuration);

services.AddLogging(builder => builder
    .SetMinimumLevel(LogLevel.None));  // Disable all framework logging - game uses GameConsole

// Memory Indexer
services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Storage.ConnectionString = "Data Source=game_memory.db";
    options.Embedding.Provider = EmbeddingProvider.OpenAI;
    options.Embedding.Model = embeddingModel;
    options.Embedding.ApiKey = openAiKey;
    options.Embedding.Dimensions = 1536;
    options.Deduplication.Enabled = false;

    // Disable reranking - LMSupply v0.8.6 tokenizer type mismatch issue
    options.Search.EnableReranking = false;
});

// OpenAI ChatClient (official SDK)
services.AddSingleton(new ChatClient(model: llmModel, apiKey: openAiKey));
services.AddSingleton<LlmClient>();

// Game components
services.AddSingleton<ToolCallParser>();
services.AddSingleton<ToolCallExecutor>();
services.AddSingleton<AlphaAgent>();
services.AddSingleton<BetaAgent>();
services.AddSingleton<GameState>();
services.AddSingleton<GameRunner>();

var serviceProvider = services.BuildServiceProvider();

// 3. Load prompts
var promptsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Prompts");
var alphaPrompt = await File.ReadAllTextAsync(Path.Combine(promptsDir, "AlphaSystemPrompt.md"));
var betaPrompt = await File.ReadAllTextAsync(Path.Combine(promptsDir, "BetaSystemPrompt.md"));

// 4. Initialize agents
var alphaAgent = serviceProvider.GetRequiredService<AlphaAgent>();
var betaAgent = serviceProvider.GetRequiredService<BetaAgent>();

alphaAgent.Initialize(alphaPrompt);
betaAgent.Initialize(betaPrompt);

// Get services for memory operations
var memoryPrimitives = serviceProvider.GetRequiredService<IMemoryPrimitives>();
var memoryStore = serviceProvider.GetRequiredService<IMemoryStore>();
var gameRunner = serviceProvider.GetRequiredService<GameRunner>();
var gameState = serviceProvider.GetRequiredService<GameState>();

// Reset function for benchmark mode
async Task ResetGameStateAsync()
{
    // Clear memories
    await ClearUserMemoriesAsync(memoryStore, GameConfiguration.AlphaUserId, GameConfiguration.AlphaSessionId);
    await ClearUserMemoriesAsync(memoryStore, GameConfiguration.BetaUserId, GameConfiguration.BetaSessionId);

    // Store initial memories
    await StoreInitialMemoriesAsync(memoryPrimitives);

    // Reset game state
    gameState.Reset();
}

// 5. Run game(s)
if (benchmarkMode)
{
    // Benchmark mode: run multiple games
    var benchmarkRunner = new BenchmarkRunner(gameRunner, ResetGameStateAsync);
    var result = await benchmarkRunner.RunAsync(iterations);
    await benchmarkRunner.OutputResultsAsync(result, outputPath);
}
else
{
    // Normal mode: single game
    Console.WriteLine("\n[INIT] Clearing previous game memories...");
    await ResetGameStateAsync();

    Console.WriteLine("[INIT] Starting game...\n");
    await gameRunner.RunAsync();

    Console.WriteLine("\n[CLEANUP] Game completed. Database: game_memory.db");
}

// ═══════════════════════════════════════════════════════════════════════════════

static void PrintHelp()
{
    Console.WriteLine("""
        Twenty Questions Game - Memory Indexer Demo

        Usage: dotnet run [options]

        Options:
          -b, --benchmark      Enable benchmark mode
          -n, --iterations N   Number of games to run (default: 1)
          -o, --output FILE    Output benchmark results to JSON file
          -h, --help           Show this help message

        Examples:
          dotnet run                           # Single game
          dotnet run --benchmark               # Single benchmark game
          dotnet run -b -n 10                  # 10 benchmark games
          dotnet run -b -n 5 -o results.json   # 5 games, save to JSON
        """);
}

static int? GetIntArg(string[] args, string longName, string shortName)
{
    var index = Array.FindIndex(args, a => a == longName || a == shortName);
    if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value))
        return value;
    return null;
}

static string? GetStringArg(string[] args, string longName, string shortName)
{
    var index = Array.FindIndex(args, a => a == longName || a == shortName);
    if (index >= 0 && index + 1 < args.Length)
        return args[index + 1];
    return null;
}

static async Task ClearUserMemoriesAsync(IMemoryStore store, string userId, string sessionId)
{
    var memories = await store.GetAllAsync(userId, new MemoryFilterOptions { SessionId = sessionId });
    foreach (var memory in memories)
    {
        await store.DeleteAsync(memory.Id, hardDelete: true);
    }
}

static async Task StoreInitialMemoriesAsync(IMemoryPrimitives memory)
{
    // Alpha's initial memory (no secret - will be generated by LLM on Round 1)
    await memory.EncodeAsync(new EncodeRequest
    {
        UserId = GameConfiguration.AlphaUserId,
        SessionId = GameConfiguration.AlphaSessionId,
        Content = "[GAME_RULES] I am Alpha, the QuizMaster. I answer 'Yes', 'No', or 'Maybe' to questions about my secret.",
        Scope = Scope.Session,
        Tier = Tier.Long
    });

    // Beta's initial memory
    await memory.EncodeAsync(new EncodeRequest
    {
        UserId = GameConfiguration.BetaUserId,
        SessionId = GameConfiguration.BetaSessionId,
        Content = "[GAME_RULES] I am Beta, the Guesser. I must identify Alpha's secret within 20 yes/no questions.",
        Scope = Scope.Session,
        Tier = Tier.Long
    });

    await memory.EncodeAsync(new EncodeRequest
    {
        UserId = GameConfiguration.BetaUserId,
        SessionId = GameConfiguration.BetaSessionId,
        Content = "[STRATEGY] Use binary search: start broad (living/non-living), then narrow by category.",
        Scope = Scope.Session,
        Tier = Tier.Long
    });
}
