using DotNetEnv;
using LMSupply.Embedder;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using SharedLib.Embedding;
using TwentyQuestionsGame.Agents;
using TwentyQuestionsGame.Benchmark;
using TwentyQuestionsGame.Game;
using TwentyQuestionsGame.LLM;
using TwentyQuestionsGame.ToolCall;
using MemoryIndexer.Configuration;
using CachingEmbeddingService = MemoryIndexer.Services.CachingEmbeddingService;

// ═══════════════════════════════════════════════════════════════════════════════
//  Twenty Questions Game - Memory Indexer Demo
//  AI vs AI: Alpha generates secret, Beta guesses through memory-only context
// ═══════════════════════════════════════════════════════════════════════════════

// Parse CLI arguments
var benchmarkMode = args.Contains("--benchmark") || args.Contains("-b");
var useLocalLlm = args.Contains("--local") || args.Contains("-l");
var debugPrompts = args.Contains("--debug") || args.Contains("-d");
var iterations = GetIntArg(args, "--iterations", "-n") ?? 1;
var outputPath = GetStringArg(args, "--output", "-o");
// LMSupply presets: "default", "fast", "quality", "small" or full HuggingFace model ID
var localModelId = GetStringArg(args, "--model", "-m") ?? "default";

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

// GPUStack configuration (primary LLM)
var gpuStackUrl = Environment.GetEnvironmentVariable("GPUSTACK_URL");
var gpuStackApiKey = Environment.GetEnvironmentVariable("GPUSTACK_APIKEY");
var gpuStackModel = Environment.GetEnvironmentVariable("GPUSTACK_MODEL") ?? "gpt-oss-20b";

// OpenAI configuration (embedding + fallback LLM)
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var embeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? "text-embedding-3-small";

// Determine LLM provider priority: --local > GPUStack > OpenAI
var useGpuStack = !useLocalLlm && !string.IsNullOrEmpty(gpuStackUrl) && !string.IsNullOrEmpty(gpuStackApiKey);
var useOpenAi = !useLocalLlm && !useGpuStack && !string.IsNullOrEmpty(openAiKey);

// Require at least one provider
if (!useLocalLlm && !useGpuStack && !useOpenAi)
{
    throw new InvalidOperationException("No LLM provider available. Set GPUSTACK_URL/GPUSTACK_APIKEY, OPENAI_API_KEY, or use --local flag.");
}

string llmProvider;
string llmModel;
if (useLocalLlm)
{
    llmProvider = "LMSupply (Local)";
    llmModel = localModelId;
}
else if (useGpuStack)
{
    llmProvider = "GPUStack";
    llmModel = gpuStackModel;
}
else
{
    llmProvider = "OpenAI";
    llmModel = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gpt-4o-mini";
}

Console.WriteLine($"[CONFIG] LLM Provider: {llmProvider}");
Console.WriteLine($"[CONFIG] LLM Model: {llmModel}");
Console.WriteLine($"[CONFIG] Embedding: {(useLocalLlm ? "default (LMSupply Local)" : embeddingModel)}");
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

// Embedding service: register BEFORE AddMemoryIndexer (uses TryAddSingleton)
// --local flag forces local embedding regardless of OpenAI key
int embeddingDimensions;
if (!useLocalLlm && !string.IsNullOrEmpty(openAiKey))
{
    // Use OpenAI embedding from SharedLib, wrapped with caching
    var openAi = new OpenAIEmbeddingService(
        apiKey: openAiKey,
        model: embeddingModel,
        dimensions: 1536);
    var cached = new CachingEmbeddingService(openAi, new EmbeddingCacheOptions
    {
        Ttl = TimeSpan.FromMinutes(30),
        MaxSize = 5000
    });
    services.AddSingleton<IEmbeddingService>(cached);
    embeddingDimensions = 1536;
}
else
{
    // Use LMSupply local embedding directly
    Console.WriteLine("[CONFIG] Using local embedding (bge-small-en-v1.5)");
    var localEmbedder = await LocalEmbedder.LoadAsync("default");  // bge-small-en-v1.5, 384 dims
    embeddingDimensions = localEmbedder.Dimensions;
    services.AddSingleton<IEmbeddingService>(new LMSupplyEmbeddingService(localEmbedder));
}

// Memory Indexer with SQLite persistent storage
services.AddMemoryIndexer(options =>
{
    options.Storage.ConnectionString = "Data Source=game_memory.db";
    options.Deduplication.Enabled = false;
    options.Search.EnableReranking = false;

    // Embedding service is already registered above
    options.Embedding.Dimensions = embeddingDimensions;
}).WithSqliteVec();

// LLM Provider Registration (LMSupply / GPUStack / OpenAI)
IGeneratorModel? localModel = null;
if (useLocalLlm)
{
    Console.WriteLine($"[INIT] Loading local model: {localModelId}...");

    // Use TextGeneratorBuilder for preset support
    var builder = TextGeneratorBuilder.Create();

    // Check if it's a preset alias or full model ID
    if (Enum.TryParse<GeneratorModelPreset>(localModelId, ignoreCase: true, out var preset))
    {
        builder.WithModel(preset);
        Console.WriteLine($"[INIT] Using LMSupply preset: {preset}");
    }
    else
    {
        builder.WithHuggingFaceModel(localModelId);
        Console.WriteLine($"[INIT] Using HuggingFace model: {localModelId}");
    }

    localModel = await builder.BuildAsync();
    await localModel.WarmupAsync();
    Console.WriteLine($"[INIT] Local model loaded (max context: {localModel.MaxContextLength})");
    services.AddSingleton(localModel);
    services.AddSingleton(sp => new LlmClient(sp.GetRequiredService<IGeneratorModel>()));
}
else if (useGpuStack)
{
    var gpuStackCredential = new System.ClientModel.ApiKeyCredential(gpuStackApiKey!);
    var gpuStackOptions = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(gpuStackUrl!) };
    var gpuStackClient = new OpenAI.OpenAIClient(gpuStackCredential, gpuStackOptions);
    services.AddSingleton(gpuStackClient.GetChatClient(llmModel));
    services.AddSingleton(sp => new LlmClient(sp.GetRequiredService<ChatClient>()));
}
else
{
    services.AddSingleton(new ChatClient(model: llmModel, apiKey: openAiKey!));
    services.AddSingleton(sp => new LlmClient(sp.GetRequiredService<ChatClient>()));
}

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

// Enable debug prompts if requested
LlmClient.DebugPrompts = debugPrompts;

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
          -d, --debug          Show full LLM prompts (for debugging)
          -l, --local          Use LMSupply local model (no API key needed)
          -m, --model MODEL    Specify local model ID (default: microsoft/Phi-4-mini-instruct-onnx)
          -n, --iterations N   Number of games to run (default: 1)
          -o, --output FILE    Output benchmark results to JSON file
          -h, --help           Show this help message

        LLM Provider Priority:
          1. --local flag → LMSupply (local ONNX model)
          2. GPUSTACK_URL + GPUSTACK_APIKEY env → GPUStack
          3. OPENAI_API_KEY env → OpenAI

        Examples:
          dotnet run                           # GPUStack/OpenAI (auto-detect)
          dotnet run --local                   # Local Phi-4 model
          dotnet run -l -m "microsoft/Phi-3-mini-4k-instruct-onnx"
          dotnet run --benchmark               # Benchmark with default provider
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
