using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Configuration;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// ============================================================================
// Twenty Questions Game - AI vs AI Demo
// ============================================================================
// 이 데모는 memory-indexer의 핵심 기능을 증명합니다:
//
// 각 AI는 상대방의 마지막 응답 1개만 받습니다.
// 이전 대화 히스토리는 전혀 전달되지 않습니다.
// 오직 memory-indexer에서 recall한 기억만으로 게임을 진행합니다.
//
// Alpha: "Yes" 응답 → Beta는 "Yes"만 받음 (히스토리 없음)
// Beta: 질문 → Alpha는 그 질문만 받음 (히스토리 없음)
// ============================================================================

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          Twenty Questions Game - Memory Demo                  ║");
Console.WriteLine("║          AI vs AI: 상대 응답 1개만 + Memory Recall            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

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
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
// GPT-5 series: gpt-5-nano (fastest), gpt-5-mini (balanced), gpt-5.2 (complex)
var openAiModel = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? "gpt-5-nano";

if (string.IsNullOrWhiteSpace(openAiApiKey))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("[ERROR] OPENAI_API_KEY must be set in .env file");
    Console.ResetColor();
    return;
}

Console.WriteLine($"[CONFIG] LLM: OpenAI {openAiModel}");
Console.WriteLine($"[CONFIG] Embedding: OpenAI text-embedding-3-small");
Console.WriteLine();

// Setup services with proper configuration
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>())
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging();

services.AddMemoryIndexer(options =>
{
    options.Storage.Type = StorageType.SqliteVec;
    options.Storage.ConnectionString = "twenty_questions.db";

    // Use OpenAI embedding
    options.Embedding.Provider = EmbeddingProvider.OpenAI;
    options.Embedding.ApiKey = openAiApiKey;
    options.Embedding.Model = "text-embedding-3-small";
    options.Embedding.Dimensions = 1536;
    options.Storage.VectorDimensions = 1536;
});

services.AddHttpClient("LLM", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
    // Increased timeout to handle longer contexts in later rounds
    client.Timeout = TimeSpan.FromSeconds(120);
});

var serviceProvider = services.BuildServiceProvider();
var memoryService = serviceProvider.GetRequiredService<MemoryService>();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

// Game configuration
const string ALPHA_USER_ID = "alpha_quizmaster";
const string BETA_USER_ID = "beta_guesser";
const int MAX_ROUNDS = 20;
// Note: Score = (vector_similarity + combined_score) / 2, can exceed 1.0
// Set high threshold to only catch near-identical questions
const float HIGH_SIMILARITY_THRESHOLD = 1.75f;

// Metrics tracking
var metrics = new GameMetrics();
var gameStopwatch = Stopwatch.StartNew();

// Generate a random secret for Alpha
var secrets = new[]
{
    "a golden retriever", "the Eiffel Tower", "a cup of coffee",
    "the moon", "a red apple", "a basketball", "a piano",
    "a sunflower", "the ocean", "a bicycle", "a rainbow",
    "a chocolate cake", "a penguin", "Mount Everest", "a guitar"
};
var secret = secrets[Random.Shared.Next(secrets.Length)];

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  GAME START                                                   ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"[SECRET] Alpha is thinking of: \"{secret}\" (hidden from Beta)");
Console.ResetColor();
Console.WriteLine();

// Clean previous game memories
Console.WriteLine("[INIT] Clearing previous game memories...");
var oldAlpha = await memoryService.GetAllAsync(ALPHA_USER_ID);
var oldBeta = await memoryService.GetAllAsync(BETA_USER_ID);
foreach (var m in oldAlpha) await memoryService.DeleteAsync(m.Id, hardDelete: true);
foreach (var m in oldBeta) await memoryService.DeleteAsync(m.Id, hardDelete: true);

// Initialize Alpha's memory with the secret
await memoryService.StoreAsync(
    ALPHA_USER_ID,
    $"[GAME_SECRET] My secret answer is: {secret}. I must not reveal this directly.",
    MemoryType.Semantic,
    importance: 1.0f);

await memoryService.StoreAsync(
    ALPHA_USER_ID,
    "[GAME_RULES] I am Alpha, the QuizMaster playing 20 Questions. " +
    "I only answer 'Yes', 'No', or 'Maybe' to questions. " +
    "I track round numbers. I detect duplicate or invalid questions.",
    MemoryType.Procedural,
    importance: 1.0f);

// Initialize Beta's memory with game rules
await memoryService.StoreAsync(
    BETA_USER_ID,
    "[GAME_RULES] I am Beta, the Guesser playing 20 Questions. " +
    "I must guess Alpha's secret in 20 questions. " +
    "I ask yes/no questions to narrow down possibilities. " +
    "On round 20, I MUST make a final guess no matter what.",
    MemoryType.Procedural,
    importance: 1.0f);

await memoryService.StoreAsync(
    BETA_USER_ID,
    "[STRATEGY] Start broad: Is it alive? Is it man-made? Is it bigger than a car? " +
    "Then narrow down based on Yes/No answers.",
    MemoryType.Procedural,
    importance: 0.9f);

Console.WriteLine("[INIT] Game initialized. Starting rounds...\n");

// Game loop
var gameOver = false;
var betaWon = false;
string lastAlphaResponse = "The game has started. Ask your first question!";

for (int round = 1; round <= MAX_ROUNDS && !gameOver; round++)
{
    var roundStopwatch = Stopwatch.StartNew();
    var roundMetrics = new RoundMetrics { Round = round };

    Console.WriteLine($"══════════════════════════ Round {round}/{MAX_ROUNDS} ══════════════════════════");
    Console.WriteLine();

    // Store round info
    await memoryService.StoreAsync(
        ALPHA_USER_ID,
        $"[ROUND] Current round: {round}/{MAX_ROUNDS}",
        MemoryType.Episodic,
        importance: 0.7f);

    await memoryService.StoreAsync(
        BETA_USER_ID,
        $"[ROUND] Current round: {round}/{MAX_ROUNDS}. Remaining: {MAX_ROUNDS - round}",
        MemoryType.Episodic,
        importance: 0.7f);

    // ═══════════════════════════════════════════════════════════════════════
    // BETA's TURN: Receives ONLY Alpha's last response (no history!)
    // ═══════════════════════════════════════════════════════════════════════
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[BETA] Received from Alpha: \"{lastAlphaResponse}\"");
    Console.WriteLine("[BETA] Recalling memories to understand context...");
    Console.ResetColor();

    // Beta recalls its own memories to understand game state
    var betaRecallSw = Stopwatch.StartNew();
    var betaMemories = await memoryService.RecallAsync(
        BETA_USER_ID,
        $"game rules strategy previous questions answers deductions round {round}",
        limit: 15);
    betaRecallSw.Stop();
    roundMetrics.BetaRecallMs = betaRecallSw.ElapsedMilliseconds;

    var betaContext = string.Join("\n", betaMemories.Select(m =>
        $"[{m.Memory.Type}, score:{m.Score:F2}] {m.Memory.Content}"));
    roundMetrics.BetaContextChars = betaContext.Length;

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"[BETA] Recalled {betaMemories.Count} memories (⏱️ {roundMetrics.BetaRecallMs}ms, 📝 {roundMetrics.BetaContextChars:N0} chars):");
    foreach (var mem in betaMemories.Take(5)) // Show top 5 memories
    {
        var shortContent = mem.Memory.Content.Length > 60
            ? mem.Memory.Content[..60] + "..."
            : mem.Memory.Content;
        Console.WriteLine($"       [{mem.Score:F2}] {shortContent}");
    }
    if (betaMemories.Count > 5) Console.WriteLine($"       ... and {betaMemories.Count - 5} more");
    Console.ResetColor();

    // Beta generates a question using ONLY last message + recalled memories
    bool isFinalRound = round == MAX_ROUNDS;
    string betaSystemPrompt = $@"You are Beta, playing 20 Questions.

YOUR MEMORIES (recalled from memory-indexer):
{betaContext}

RULES:
- You just received Alpha's response: ""{lastAlphaResponse}""
- You have NO access to previous conversation - only your memories above
- {(isFinalRound ? "This is round 20 - you MUST make your final guess NOW!" : $"This is round {round}. Ask a strategic yes/no question.")}
- Do NOT repeat questions you've already asked (check your memories)
- Build on previous Yes/No answers to narrow down";

    string betaUserMessage = lastAlphaResponse; // ONLY the last response!

    string betaQuestion;
    LLMMetrics betaLlmMetrics;
    if (isFinalRound)
    {
        (betaQuestion, betaLlmMetrics) = await CallLLMWithMetricsAsync(
            httpClientFactory, openAiModel,
            betaSystemPrompt,
            $"Alpha said: \"{betaUserMessage}\". This is your FINAL turn! Make your best guess: 'My final guess is: [answer]'");
    }
    else
    {
        (betaQuestion, betaLlmMetrics) = await CallLLMWithMetricsAsync(
            httpClientFactory, openAiModel,
            betaSystemPrompt,
            $"Alpha said: \"{betaUserMessage}\". Based on your memories, ask ONE strategic yes/no question. Just write the question, nothing else:");
    }

    roundMetrics.BetaLlmMs = betaLlmMetrics.DurationMs;
    roundMetrics.BetaPromptTokens = betaLlmMetrics.PromptTokens;
    roundMetrics.BetaCompletionTokens = betaLlmMetrics.CompletionTokens;

    // Handle LLM failure after all retries
    if (betaQuestion.StartsWith("Error:"))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[BETA] LLM failed to generate question after retries. Skipping round.");
        Console.ResetColor();
        lastAlphaResponse = "Please ask a question.";
        continue;
    }

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[BETA] >>> {betaQuestion}");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"       ⏱️ LLM: {roundMetrics.BetaLlmMs}ms | 🎯 Prompt: {roundMetrics.BetaPromptTokens} | 💬 Completion: {roundMetrics.BetaCompletionTokens}");
    Console.ResetColor();

    // Store Beta's question in Beta's memory
    await memoryService.StoreAsync(
        BETA_USER_ID,
        $"[MY_QUESTION_R{round}] I asked: {betaQuestion}",
        MemoryType.Episodic,
        importance: 0.9f);

    // ═══════════════════════════════════════════════════════════════════════
    // ALPHA's TURN: Receives ONLY Beta's question (no history!)
    // ═══════════════════════════════════════════════════════════════════════
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine($"[ALPHA] Received question: \"{betaQuestion}\"");
    Console.WriteLine("[ALPHA] Recalling memories...");
    Console.ResetColor();

    // Alpha recalls its memories (secret, rules, previous Q&A)
    var alphaRecallSw = Stopwatch.StartNew();
    var alphaMemories = await memoryService.RecallAsync(
        ALPHA_USER_ID,
        $"secret rules previous questions answers {betaQuestion}",
        limit: 15);
    alphaRecallSw.Stop();
    roundMetrics.AlphaRecallMs = alphaRecallSw.ElapsedMilliseconds;

    var alphaContext = string.Join("\n", alphaMemories.Select(m =>
        $"[{m.Memory.Type}, score:{m.Score:F2}] {m.Memory.Content}"));
    roundMetrics.AlphaContextChars = alphaContext.Length;

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"[ALPHA] Recalled {alphaMemories.Count} memories (⏱️ {roundMetrics.AlphaRecallMs}ms, 📝 {roundMetrics.AlphaContextChars:N0} chars):");
    foreach (var mem in alphaMemories.Take(3)) // Show top 3 memories
    {
        var shortContent = mem.Memory.Content.Length > 60
            ? mem.Memory.Content[..60] + "..."
            : mem.Memory.Content;
        Console.WriteLine($"        [{mem.Score:F2}] {shortContent}");
    }
    Console.ResetColor();

    // Check for final guess first (thin logic - just pattern match)
    var questionLower = betaQuestion.ToLower();
    var secretLower = secret.ToLower().Replace("a ", "").Replace("an ", "").Replace("the ", "");

    if (questionLower.Contains("guess") || questionLower.Contains("answer is"))
    {
        // Extract guess and compare with secret
        var guessMatch = questionLower.Contains(secretLower) ||
                         secretLower.Split(' ').Any(w => w.Length > 3 && questionLower.Contains(w));

        if (guessMatch)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ALPHA] >>> CORRECT! You guessed it!");
            Console.ResetColor();
            betaWon = true;
            gameOver = true;

            roundStopwatch.Stop();
            roundMetrics.TotalRoundMs = roundStopwatch.ElapsedMilliseconds;
            metrics.Rounds.Add(roundMetrics);
            break;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[ALPHA] >>> Wrong guess. Keep trying!");
            Console.ResetColor();
            lastAlphaResponse = "Wrong guess. Keep trying!";

            roundStopwatch.Stop();
            roundMetrics.TotalRoundMs = roundStopwatch.ElapsedMilliseconds;
            metrics.Rounds.Add(roundMetrics);
            PrintRoundSummary(roundMetrics);
            continue;
        }
    }

    // Check for duplicate questions using semantic similarity
    var dupCheckSw = Stopwatch.StartNew();
    var duplicateCheck = await memoryService.RecallAsync(
        ALPHA_USER_ID,
        betaQuestion,
        limit: 5);
    dupCheckSw.Stop();
    roundMetrics.DuplicateCheckMs = dupCheckSw.ElapsedMilliseconds;

    var similarQuestion = duplicateCheck.FirstOrDefault(m =>
        m.Memory.Content.Contains("[QUESTION_R") &&
        m.Score > HIGH_SIMILARITY_THRESHOLD);

    string alphaResponse;
    if (similarQuestion != null)
    {
        alphaResponse = $"INVALID: This is too similar to a previous question. Score: {similarQuestion.Score:F2}. Ask something different.";
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ALPHA] Duplicate detected! Similarity: {similarQuestion.Score:F2}");
        Console.ResetColor();
    }
    else
    {
        // Store the question in Alpha's memory
        await memoryService.StoreAsync(
            ALPHA_USER_ID,
            $"[QUESTION_R{round}] Beta asked: {betaQuestion}",
            MemoryType.Episodic,
            importance: 0.9f);

        // Alpha generates response using ONLY the question + recalled memories
        string alphaSystemPrompt = $@"You are Alpha, the QuizMaster in 20 Questions.

YOUR MEMORIES (recalled from memory-indexer):
{alphaContext}

RULES:
- You just received this question: ""{betaQuestion}""
- You have NO access to previous conversation - only your memories above
- Answer ONLY: 'Yes', 'No', or 'Maybe'
- If the question is not a proper yes/no question, say 'INVALID: [reason]'
- Be honest based on your secret (which should be in your memories)";

        LLMMetrics alphaLlmMetrics;
        (alphaResponse, alphaLlmMetrics) = await CallLLMWithMetricsAsync(
            httpClientFactory, openAiModel,
            alphaSystemPrompt,
            betaQuestion); // ONLY the question!

        roundMetrics.AlphaLlmMs = alphaLlmMetrics.DurationMs;
        roundMetrics.AlphaPromptTokens = alphaLlmMetrics.PromptTokens;
        roundMetrics.AlphaCompletionTokens = alphaLlmMetrics.CompletionTokens;

        // Normalize response
        alphaResponse = NormalizeResponse(alphaResponse);

        // Store Alpha's answer
        await memoryService.StoreAsync(
            ALPHA_USER_ID,
            $"[ANSWER_R{round}] I answered '{alphaResponse}' to: {betaQuestion}",
            MemoryType.Episodic,
            importance: 0.85f);
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[ALPHA] >>> {alphaResponse}");
    if (roundMetrics.AlphaLlmMs > 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"        ⏱️ LLM: {roundMetrics.AlphaLlmMs}ms | 🎯 Prompt: {roundMetrics.AlphaPromptTokens} | 💬 Completion: {roundMetrics.AlphaCompletionTokens}");
    }
    Console.ResetColor();

    // Store the exchange in Beta's memory
    await memoryService.StoreAsync(
        BETA_USER_ID,
        $"[QA_R{round}] Q: {betaQuestion} -> A: {alphaResponse}",
        MemoryType.Episodic,
        importance: 0.95f);

    // Store deduction if valid answer
    if (!alphaResponse.StartsWith("INVALID"))
    {
        string deduction;
        if (alphaResponse.StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
            deduction = $"[DEDUCTION_R{round}] CONFIRMED: The secret HAS the property asked in '{betaQuestion}'";
        else if (alphaResponse.StartsWith("No", StringComparison.OrdinalIgnoreCase))
            deduction = $"[DEDUCTION_R{round}] RULED OUT: The secret does NOT have the property asked in '{betaQuestion}'";
        else
            deduction = $"[DEDUCTION_R{round}] UNCERTAIN: The secret may or may not have the property asked in '{betaQuestion}'";

        await memoryService.StoreAsync(
            BETA_USER_ID,
            deduction,
            MemoryType.Semantic,
            importance: 0.9f);
    }

    // Update lastAlphaResponse for next round
    lastAlphaResponse = alphaResponse;

    roundStopwatch.Stop();
    roundMetrics.TotalRoundMs = roundStopwatch.ElapsedMilliseconds;
    metrics.Rounds.Add(roundMetrics);

    PrintRoundSummary(roundMetrics);
    Console.WriteLine();
    await Task.Delay(500);
}

gameStopwatch.Stop();
metrics.TotalGameMs = gameStopwatch.ElapsedMilliseconds;

// Game Over Summary
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  GAME OVER                                                    ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

if (betaWon)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  🎉 BETA WINS! Successfully guessed: {secret}");
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  👑 ALPHA WINS! Beta failed to guess: {secret}");
}
Console.ResetColor();
Console.WriteLine();

// Show memory statistics
var alphaMemoryCount = (await memoryService.GetAllAsync(ALPHA_USER_ID)).Count;
var betaMemoryCount = (await memoryService.GetAllAsync(BETA_USER_ID)).Count;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  MEMORY STATISTICS                                            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  Alpha memories: {alphaMemoryCount}");
Console.WriteLine($"  Beta memories:  {betaMemoryCount}");
Console.WriteLine($"  Total:          {alphaMemoryCount + betaMemoryCount}");
Console.WriteLine();

// Performance Statistics
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  PERFORMANCE STATISTICS                                       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  Total game time:      {metrics.TotalGameMs:N0}ms ({metrics.TotalGameMs / 1000.0:F1}s)");
Console.WriteLine($"  Rounds played:        {metrics.Rounds.Count}");
Console.WriteLine($"  Avg round time:       {metrics.Rounds.Average(r => r.TotalRoundMs):N0}ms");
Console.WriteLine();

Console.WriteLine("  ┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("  │ RECALL PERFORMANCE                                          │");
Console.WriteLine("  ├─────────────────────────────────────────────────────────────┤");
Console.WriteLine($"  │ Total recall time:    {metrics.Rounds.Sum(r => r.BetaRecallMs + r.AlphaRecallMs):N0}ms");
Console.WriteLine($"  │ Avg Beta recall:      {metrics.Rounds.Average(r => r.BetaRecallMs):N0}ms");
Console.WriteLine($"  │ Avg Alpha recall:     {metrics.Rounds.Average(r => r.AlphaRecallMs):N0}ms");
Console.WriteLine($"  │ Max recall time:      {metrics.Rounds.Max(r => Math.Max(r.BetaRecallMs, r.AlphaRecallMs)):N0}ms");
Console.WriteLine("  └─────────────────────────────────────────────────────────────┘");
Console.WriteLine();

Console.WriteLine("  ┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("  │ LLM PERFORMANCE                                             │");
Console.WriteLine("  ├─────────────────────────────────────────────────────────────┤");
Console.WriteLine($"  │ Total LLM time:       {metrics.Rounds.Sum(r => r.BetaLlmMs + r.AlphaLlmMs):N0}ms ({metrics.Rounds.Sum(r => r.BetaLlmMs + r.AlphaLlmMs) / 1000.0:F1}s)");
Console.WriteLine($"  │ Avg Beta LLM:         {metrics.Rounds.Average(r => r.BetaLlmMs):N0}ms");
Console.WriteLine($"  │ Avg Alpha LLM:        {metrics.Rounds.Where(r => r.AlphaLlmMs > 0).DefaultIfEmpty(new RoundMetrics()).Average(r => r.AlphaLlmMs):N0}ms");
Console.WriteLine($"  │ Max LLM time:         {metrics.Rounds.Max(r => Math.Max(r.BetaLlmMs, r.AlphaLlmMs)):N0}ms");
Console.WriteLine("  └─────────────────────────────────────────────────────────────┘");
Console.WriteLine();

Console.WriteLine("  ┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("  │ TOKEN USAGE                                                 │");
Console.WriteLine("  ├─────────────────────────────────────────────────────────────┤");
var totalPromptTokens = metrics.Rounds.Sum(r => r.BetaPromptTokens + r.AlphaPromptTokens);
var totalCompletionTokens = metrics.Rounds.Sum(r => r.BetaCompletionTokens + r.AlphaCompletionTokens);
Console.WriteLine($"  │ Total prompt tokens:  {totalPromptTokens:N0}");
Console.WriteLine($"  │ Total completion:     {totalCompletionTokens:N0}");
Console.WriteLine($"  │ Total tokens:         {totalPromptTokens + totalCompletionTokens:N0}");
Console.WriteLine($"  │ Avg prompt/round:     {metrics.Rounds.Average(r => r.BetaPromptTokens + r.AlphaPromptTokens):N0}");
Console.WriteLine($"  │ Avg completion/round: {metrics.Rounds.Average(r => r.BetaCompletionTokens + r.AlphaCompletionTokens):N0}");
Console.WriteLine("  └─────────────────────────────────────────────────────────────┘");
Console.WriteLine();

Console.WriteLine("  ┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("  │ CONTEXT SIZE (Characters)                                   │");
Console.WriteLine("  ├─────────────────────────────────────────────────────────────┤");
Console.WriteLine($"  │ Avg Beta context:     {metrics.Rounds.Average(r => r.BetaContextChars):N0} chars");
Console.WriteLine($"  │ Avg Alpha context:    {metrics.Rounds.Average(r => r.AlphaContextChars):N0} chars");
Console.WriteLine($"  │ Max context size:     {metrics.Rounds.Max(r => Math.Max(r.BetaContextChars, r.AlphaContextChars)):N0} chars");
Console.WriteLine("  └─────────────────────────────────────────────────────────────┘");
Console.WriteLine();

// Per-round breakdown
Console.WriteLine("  ┌───────┬──────────┬──────────┬──────────┬──────────┬──────────┐");
Console.WriteLine("  │ Round │ Recall   │ LLM      │ Prompt   │ Complet. │ Total    │");
Console.WriteLine("  ├───────┼──────────┼──────────┼──────────┼──────────┼──────────┤");
foreach (var r in metrics.Rounds)
{
    var recallMs = r.BetaRecallMs + r.AlphaRecallMs;
    var llmMs = r.BetaLlmMs + r.AlphaLlmMs;
    var prompt = r.BetaPromptTokens + r.AlphaPromptTokens;
    var completion = r.BetaCompletionTokens + r.AlphaCompletionTokens;
    Console.WriteLine($"  │  {r.Round,2}   │ {recallMs,6}ms │ {llmMs,6}ms │ {prompt,6}   │ {completion,6}   │ {r.TotalRoundMs,6}ms │");
}
Console.WriteLine("  └───────┴──────────┴──────────┴──────────┴──────────┴──────────┘");
Console.WriteLine();

Console.WriteLine("  ┌────────────────────────────────────────────────────────┐");
Console.WriteLine("  │ KEY DEMONSTRATION:                                     │");
Console.WriteLine("  │ - Each LLM call received ONLY the opponent's last msg  │");
Console.WriteLine("  │ - NO chat history was passed                           │");
Console.WriteLine("  │ - Context came 100% from memory-indexer recall         │");
Console.WriteLine("  └────────────────────────────────────────────────────────┘");
Console.WriteLine();

// Cleanup option
Console.Write("Delete game memories? (y/N): ");
var cleanup = Console.ReadLine()?.Trim().ToLower();
if (cleanup == "y" || cleanup == "yes")
{
    var all1 = await memoryService.GetAllAsync(ALPHA_USER_ID);
    var all2 = await memoryService.GetAllAsync(BETA_USER_ID);
    foreach (var m in all1) await memoryService.DeleteAsync(m.Id, hardDelete: true);
    foreach (var m in all2) await memoryService.DeleteAsync(m.Id, hardDelete: true);
    Console.WriteLine("Game memories deleted.");
}

Console.WriteLine("\nThank you for playing!");

// ============================================================================
// Helper Functions
// ============================================================================

void PrintRoundSummary(RoundMetrics rm)
{
    var recallTotal = rm.BetaRecallMs + rm.AlphaRecallMs;
    var llmTotal = rm.BetaLlmMs + rm.AlphaLlmMs;
    var tokenTotal = rm.BetaPromptTokens + rm.AlphaPromptTokens + rm.BetaCompletionTokens + rm.AlphaCompletionTokens;

    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine($"  ⏱️ Round {rm.Round} Summary: Recall={recallTotal}ms, LLM={llmTotal}ms, Tokens={tokenTotal}, Total={rm.TotalRoundMs}ms");
    Console.ResetColor();
}

string NormalizeResponse(string response)
{
    response = response.Trim();

    // Extract Yes/No/Maybe if buried in text
    var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    var firstLine = lines.FirstOrDefault()?.Trim() ?? response;

    if (firstLine.StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
        return "Yes";
    if (firstLine.StartsWith("No", StringComparison.OrdinalIgnoreCase))
        return "No";
    if (firstLine.StartsWith("Maybe", StringComparison.OrdinalIgnoreCase))
        return "Maybe";
    if (firstLine.StartsWith("INVALID", StringComparison.OrdinalIgnoreCase))
        return firstLine;

    // Try to find Yes/No/Maybe anywhere
    if (response.Contains("Yes", StringComparison.OrdinalIgnoreCase))
        return "Yes";
    if (response.Contains("No", StringComparison.OrdinalIgnoreCase))
        return "No";
    if (response.Contains("Maybe", StringComparison.OrdinalIgnoreCase))
        return "Maybe";

    return response.Length > 100 ? response[..100] + "..." : response;
}

async Task<(string Response, LLMMetrics Metrics)> CallLLMWithMetricsAsync(
    IHttpClientFactory factory,
    string model,
    string systemPrompt,
    string userMessage,
    int maxRetries = 3)
{
    var client = factory.CreateClient("LLM");
    var baseDelay = TimeSpan.FromSeconds(2);
    var metrics = new LLMMetrics();
    var totalStopwatch = Stopwatch.StartNew();

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            var attemptSw = Stopwatch.StartNew();

            var request = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };

            var response = await client.PostAsJsonAsync("chat/completions", request);
            attemptSw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"{response.StatusCode}: {errorBody}");
            }

            var result = await response.Content.ReadFromJsonAsync<LLMResponse>();
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

            // Extract token usage
            metrics.PromptTokens = result?.Usage?.PromptTokens ?? 0;
            metrics.CompletionTokens = result?.Usage?.CompletionTokens ?? 0;
            metrics.DurationMs = attemptSw.ElapsedMilliseconds;

            // Check for valid response
            if (!string.IsNullOrWhiteSpace(content) && content != "No response")
            {
                return (content, metrics);
            }

            // Empty or invalid response - retry
            if (attempt < maxRetries)
            {
                var delay = baseDelay * Math.Pow(2, attempt - 1); // Exponential backoff
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"[LLM] Empty response, retrying ({attempt}/{maxRetries}) in {delay.TotalSeconds:F1}s...");
                Console.ResetColor();
                await Task.Delay(delay);
            }
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == default)
        {
            // Timeout
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[LLM] ⚠️ TIMEOUT after {totalStopwatch.ElapsedMilliseconds}ms (attempt {attempt}/{maxRetries})");
            Console.ResetColor();

            if (attempt < maxRetries)
            {
                var delay = baseDelay * Math.Pow(2, attempt - 1);
                Console.WriteLine($"[LLM] Retrying in {delay.TotalSeconds:F1}s...");
                await Task.Delay(delay);
            }
        }
        catch (Exception ex)
        {
            if (attempt < maxRetries)
            {
                var delay = baseDelay * Math.Pow(2, attempt - 1); // Exponential backoff
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"[LLM] Error: {ex.Message}, retrying ({attempt}/{maxRetries}) in {delay.TotalSeconds:F1}s...");
                Console.ResetColor();
                await Task.Delay(delay);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LLM ERROR] All {maxRetries} attempts failed. Last error: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    metrics.DurationMs = totalStopwatch.ElapsedMilliseconds;
    return ("Error: LLM call failed after all retries", metrics);
}

// ============================================================================
// Models
// ============================================================================

class GameMetrics
{
    public List<RoundMetrics> Rounds { get; } = new();
    public long TotalGameMs { get; set; }
}

class RoundMetrics
{
    public int Round { get; set; }

    // Recall timings
    public long BetaRecallMs { get; set; }
    public long AlphaRecallMs { get; set; }
    public long DuplicateCheckMs { get; set; }

    // LLM timings
    public long BetaLlmMs { get; set; }
    public long AlphaLlmMs { get; set; }

    // Token usage
    public int BetaPromptTokens { get; set; }
    public int BetaCompletionTokens { get; set; }
    public int AlphaPromptTokens { get; set; }
    public int AlphaCompletionTokens { get; set; }

    // Context sizes
    public int BetaContextChars { get; set; }
    public int AlphaContextChars { get; set; }

    // Total
    public long TotalRoundMs { get; set; }
}

class LLMMetrics
{
    public long DurationMs { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}

class LLMResponse
{
    [JsonPropertyName("choices")]
    public List<LLMChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public LLMUsage? Usage { get; set; }
}

class LLMChoice
{
    [JsonPropertyName("message")]
    public LLMMessage? Message { get; set; }
}

class LLMMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

class LLMUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
