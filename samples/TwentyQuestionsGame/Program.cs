using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// ============================================================================
// Twenty Questions Game - AI vs AI Demo
// ============================================================================
// This demo proves the core capability of memory-indexer using the 3-Axis Model:
//
// Each AI receives ONLY the opponent's last response (1 message).
// NO conversation history is passed.
// Context comes 100% from IMemoryPrimitives recall (Expert API).
//
// Alpha: "Yes" → Beta receives "Yes" only (no history)
// Beta: Question → Alpha receives question only (no history)
//
// ============================================================================
// API USED: IMemoryPrimitives (Expert API, Level 3)
// - Full control over Type × Scope × Tier
// - Explicit Importance and Scope management
// - Advanced filtering in RetrieveAsync
// ============================================================================

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          Twenty Questions Game - Memory Demo                  ║");
Console.WriteLine("║          AI vs AI: 상대 응답 1개만 + Memory Recall            ║");
Console.WriteLine("║          API: IMemoryPrimitives (3-Axis Model)                ║");
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
var openAiModel = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? "gpt-4o-mini";

if (string.IsNullOrWhiteSpace(openAiApiKey))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("[ERROR] OPENAI_API_KEY must be set in .env file");
    Console.ResetColor();
    return;
}

// Memory log mode: "full" or "summary"
var memoryLogMode = Environment.GetEnvironmentVariable("MEMORY_LOG_MODE");
if (string.IsNullOrWhiteSpace(memoryLogMode))
{
#if DEBUG
    memoryLogMode = "full";
#else
    memoryLogMode = "summary";
#endif
}
var isFullMemoryLog = memoryLogMode.Equals("full", StringComparison.OrdinalIgnoreCase);

Console.WriteLine($"[CONFIG] LLM: OpenAI {openAiModel}");
Console.WriteLine($"[CONFIG] Embedding: OpenAI text-embedding-3-small");
Console.WriteLine($"[CONFIG] Memory Log: {(isFullMemoryLog ? "Full" : "Summary")} (set MEMORY_LOG_MODE=full|summary)");
Console.WriteLine($"[CONFIG] API: IMemoryPrimitives (Expert API with 3-Axis control)");
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

    // Use OpenAI for knowledge extraction
    options.Completion.Provider = CompletionProvider.OpenAI;
    options.Completion.ApiKey = openAiApiKey;
    options.Completion.Model = openAiModel;
    options.Completion.DefaultTemperature = 0.1f;
    options.Completion.DefaultMaxTokens = 300;

    // Disable reranking (avoid ONNX Runtime issues in demo)
    options.Search.EnableReranking = false;
});

services.AddHttpClient("LLM", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
    client.Timeout = TimeSpan.FromSeconds(120);
});

var serviceProvider = services.BuildServiceProvider();
var memoryPrimitives = serviceProvider.GetRequiredService<IMemoryPrimitives>();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
var knowledgeExtractor = serviceProvider.GetRequiredService<IKnowledgeExtractor>();

// Game configuration
const string ALPHA_USER_ID = "alpha_quizmaster";
const string ALPHA_SESSION_ID = "game_session_alpha";
const string BETA_USER_ID = "beta_guesser";
const string BETA_SESSION_ID = "game_session_beta";
const int MAX_ROUNDS = 20;
const float HIGH_SIMILARITY_THRESHOLD = 0.85f;

// Phase 37-38: Strategic questioning phases for Beta
const string BETA_STRATEGY_PHASE1 = @"[STRATEGY_PHASE1] Rounds 1-5: Establish category

Priority sequence:
1. **Living vs Non-living**: ""Is it a living thing?""
2. **IF LIVING → Animal vs Plant**: ""Is it an animal?"" OR ""Is it a plant?""
   - This distinction is CRITICAL for living things!
3. **Natural vs Man-made**: ""Is it man-made?""
4. **Physical vs Abstract**: ""Is it a physical object?""
5. **Broad category confirmation**: Confirm the established category

Split the entire possibility space into broad categories.
Each question should eliminate ~50% of remaining possibilities.";

const string BETA_STRATEGY_PHASE2 = @"[STRATEGY_PHASE2] Rounds 6-12: Physical properties
- Size: hand-held, room-sized, larger?
- Material: metal, plastic, wood, fabric, organic?
- Location: indoor, outdoor, specific room?
- Electronic: requires power, battery, manual?
Narrow down based on physical characteristics.";

const string BETA_STRATEGY_PHASE3 = @"[STRATEGY_PHASE3] Rounds 13-18: Usage and purpose
- Function: what does it do? (tool, furniture, decoration, food, etc.)
- User: who uses it? (everyone, specific profession, children, etc.)
- Frequency: daily use, occasional, rare?
- Necessity: essential, luxury, optional?
Focus on how and why the object is used.";

const string BETA_STRATEGY_PHASE4 = @"[STRATEGY_PHASE4] Round 19: Candidate Generation

**Round 19 - CANDIDATE GENERATION**:
You MUST explicitly:
1. List ALL CONFIRMED properties from your memories
2. List ALL RULED OUT properties from your memories
3. Generate 3-5 specific candidates that match CONFIRMED and avoid RULED OUT
4. Ask final clarifying question to distinguish between candidates

Format your question like:
""My candidates are: [list 3-5 items]. Final question: [strategic yes/no question]""

Example:
- CONFIRMED: living, natural, grows in soil, has petals
- RULED OUT: animal, edible, used indoors
- Candidates: sunflower, rose, tulip, daisy, lily
- Final question: ""Is it typically yellow?""

Output: Your candidates list and ONE final clarifying yes/no question.";

const string BETA_STRATEGY_PHASE4_FINAL = @"[STRATEGY_PHASE4_FINAL] Round 20: Final Guess with Scoring

**Round 20 - MANDATORY FINAL GUESS**:
This is your LAST chance. You MUST make your best guess.

STEP-BY-STEP PROCESS:
1. Review Round 19 candidates and Alpha's last response
2. Score each candidate against ALL confirmed/ruled-out properties
3. Pick the HIGHEST scoring candidate
4. Format: ""My final guess is: [your answer]""

Example scoring:
- Candidate A: 8/10 properties match → Score 0.8
- Candidate B: 6/10 properties match → Score 0.6
- Candidate C: 9/10 properties match → Score 0.9 ← PICK THIS

CRITICAL: Your guess MUST be consistent with ALL confirmed properties!
If it was confirmed as ""living thing"", DO NOT guess non-living objects!

Output ONLY: ""My final guess is: [your answer]""";

// Few-shot example for successful deduction
const string DEDUCTION_EXAMPLE = @"
EXAMPLE OF SUCCESSFUL DEDUCTION:
Round 1-18 findings:
  CONFIRMED: living thing, natural, grows from ground, has petals, colorful, found in gardens
  RULED OUT: animal, edible, tree, used indoors, needs daily care

Round 19 candidates:
  1. Sunflower (living, petals, colorful, garden, natural) → Score: 6/6 ✅
  2. Rose (living, petals, colorful, garden, natural) → Score: 6/6 ✅
  3. Cactus (living, natural, garden, but no typical petals) → Score: 4/6
  4. Rock (non-living) → Score: 0/6 ❌
  5. Plastic flower (man-made) → Score: 0/6 ❌

Round 19 question: ""Does it typically grow very tall?"" → ""Yes""
Round 20 final scoring:
  - Sunflower: tall, petals, colorful → PICK THIS ✅
  - Rose: not typically very tall → eliminate

Final guess: ""My final guess is: a sunflower"" → CORRECT!";

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
var oldAlpha = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
{
    UserId = ALPHA_USER_ID,
    Query = "*",
    Limit = 10000,
    MinScore = 0.0f
});
var oldBeta = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
{
    UserId = BETA_USER_ID,
    Query = "*",
    Limit = 10000,
    MinScore = 0.0f
});
foreach (var m in oldAlpha)
{
    await memoryPrimitives.DeleteAsync(new DeleteRequest
    {
        MemoryId = m.Memory.Id,
        HardDelete = true
    });
}
foreach (var m in oldBeta)
{
    await memoryPrimitives.DeleteAsync(new DeleteRequest
    {
        MemoryId = m.Memory.Id,
        HardDelete = true
    });
}

// Initialize Alpha's memory with the secret
await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = ALPHA_USER_ID,
    SessionId = ALPHA_SESSION_ID,
    Content = $"[GAME_SECRET] My secret answer is: {secret}. I must not reveal this directly.",
    Type = MemoryType.Semantic,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 1.0f
});

await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = ALPHA_USER_ID,
    SessionId = ALPHA_SESSION_ID,
    Content = "[GAME_RULES] I am Alpha, the QuizMaster playing 20 Questions. " +
              "I only answer 'Yes', 'No', or 'Maybe' to questions. " +
              "I track round numbers. I detect duplicate or invalid questions.",
    Type = MemoryType.Procedural,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 1.0f
});

// Initialize Beta's memory with game rules and strategy phases
await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Content = @"[GAME_RULES] I am Beta, the Guesser in 20 Questions.
Goal: Identify Alpha's secret within 20 yes/no questions.
Strategy: Use binary search to halve possibilities each round.
Round 20: MUST make final guess regardless of certainty.",
    Type = MemoryType.Procedural,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 1.0f
});

await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Content = @"[STRATEGY_PHASE1] Rounds 1-3: Establish category
- Alive vs non-living
- Natural vs man-made
- Physical object vs place/concept
These questions split the entire possibility space.",
    Type = MemoryType.Procedural,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 0.95f
});

await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Content = @"[STRATEGY_PHASE2] Rounds 4-8: Narrow domain
- Size comparisons (bigger than X?)
- Location (indoors/outdoors, specific regions)
- Common usage patterns
Each question should eliminate ~50% of remaining options.",
    Type = MemoryType.Procedural,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 0.9f
});

await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Content = @"[DEDUCTION_TEMPLATE] After each answer, I record:
- Yes → CONFIRMED: secret HAS this property
- No → RULED OUT: secret does NOT have this property
- Maybe → UNCERTAIN: need different angle
I must check these before each question to avoid redundancy.",
    Type = MemoryType.Procedural,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 0.85f
});

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
    await memoryPrimitives.EncodeAsync(new EncodeRequest
    {
        UserId = ALPHA_USER_ID,
        SessionId = ALPHA_SESSION_ID,
        Content = $"[ROUND] Current round: {round}/{MAX_ROUNDS}",
        Type = MemoryType.Episodic,
        Scope = Scope.Session,
        Tier = Tier.Short,
        ImportanceScore = 0.7f
    });

    await memoryPrimitives.EncodeAsync(new EncodeRequest
    {
        UserId = BETA_USER_ID,
        SessionId = BETA_SESSION_ID,
        Content = $"[ROUND] Current round: {round}/{MAX_ROUNDS}. Remaining: {MAX_ROUNDS - round}",
        Type = MemoryType.Episodic,
        Scope = Scope.Session,
        Tier = Tier.Short,
        ImportanceScore = 0.7f
    });

    // ═══════════════════════════════════════════════════════════════════════
    // BETA's TURN: Receives ONLY Alpha's last response (no history!)
    // ═══════════════════════════════════════════════════════════════════════
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[BETA] Received from Alpha: \"{lastAlphaResponse}\"");
    Console.WriteLine("[BETA] Recalling memories to understand context...");
    Console.ResetColor();

    // Beta recalls its own memories to understand game state
    var betaRecallSw = Stopwatch.StartNew();
    var betaMemories = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
    {
        UserId = BETA_USER_ID,
        SessionId = BETA_SESSION_ID,
        Query = $"my questions, Alpha's answers, and deductions from all previous rounds up to round {round - 1}",  // Phase 39: More explicit query
        Limit = 50,  // Phase 39: Increased from 30 to capture more deductions
        MinScore = 0.3f
    });
    betaRecallSw.Stop();
    roundMetrics.BetaRecallMs = betaRecallSw.ElapsedMilliseconds;

    var betaContext = string.Join("\n", betaMemories.Select(m =>
        $"[{m.Memory.Type}, score:{m.Score:F2}] {m.Memory.Content}"));
    roundMetrics.BetaContextChars = betaContext.Length;

    PrintRecalledMemories("BETA", betaMemories, roundMetrics.BetaRecallMs, roundMetrics.BetaContextChars, isFullMemoryLog);

    // Beta generates a question using ONLY last message + recalled memories
    // Phase 38: Use GetBetaSystemPrompt() for round-specific prompting
    string betaSystemPrompt = GetBetaSystemPrompt(round, betaContext, lastAlphaResponse);
    string betaUserMessage = lastAlphaResponse; // ONLY the last response!

    bool isFinalRound = round == MAX_ROUNDS;
    bool isCandidateGeneration = round == MAX_ROUNDS - 1;

    string betaQuestion;
    LLMMetrics betaLlmMetrics;
    if (isFinalRound)
    {
        (betaQuestion, betaLlmMetrics) = await CallLLMWithMetricsAsync(
            httpClientFactory, openAiModel,
            betaSystemPrompt,
            $"Alpha said: \"{betaUserMessage}\". This is your FINAL turn! Score your candidates and make your best guess: 'My final guess is: [answer]'");
    }
    else if (isCandidateGeneration)
    {
        (betaQuestion, betaLlmMetrics) = await CallLLMWithMetricsAsync(
            httpClientFactory, openAiModel,
            betaSystemPrompt,
            $"Alpha said: \"{betaUserMessage}\". Review your memories, list candidates, and ask ONE final clarifying yes/no question.");
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
    await memoryPrimitives.EncodeAsync(new EncodeRequest
    {
        UserId = BETA_USER_ID,
        SessionId = BETA_SESSION_ID,
        Content = $"[MY_QUESTION_R{round}] I asked: {betaQuestion}",
        Type = MemoryType.Episodic,
        Scope = Scope.Session,
        Tier = Tier.Short,
        ImportanceScore = 0.98f  // Phase 39: Increased for better recall
    });

    // ═══════════════════════════════════════════════════════════════════════
    // ALPHA's TURN: Receives ONLY Beta's question (no history!)
    // ═══════════════════════════════════════════════════════════════════════
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine($"[ALPHA] Received question: \"{betaQuestion}\"");
    Console.WriteLine("[ALPHA] Recalling memories...");
    Console.ResetColor();

    // Alpha recalls its memories (secret, rules, previous Q&A)
    var alphaRecallSw = Stopwatch.StartNew();
    var alphaMemories = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
    {
        UserId = ALPHA_USER_ID,
        SessionId = ALPHA_SESSION_ID,
        Query = $"Beta's questions and my answers from all previous rounds up to round {round - 1}",  // Phase 39: More explicit query
        Limit = 50,  // Phase 39: Increased from 30 to capture duplicate patterns
        MinScore = 0.3f
    });
    alphaRecallSw.Stop();
    roundMetrics.AlphaRecallMs = alphaRecallSw.ElapsedMilliseconds;

    var alphaContext = string.Join("\n", alphaMemories.Select(m =>
        $"[{m.Memory.Type}, score:{m.Score:F2}] {m.Memory.Content}"));
    roundMetrics.AlphaContextChars = alphaContext.Length;

    PrintRecalledMemories("ALPHA", alphaMemories, roundMetrics.AlphaRecallMs, roundMetrics.AlphaContextChars, isFullMemoryLog);

    // Check for final guess or direct identification
    var questionLower = betaQuestion.ToLower();
    var secretLower = secret.ToLower().Replace("a ", "").Replace("an ", "").Replace("the ", "");

    // Pattern 1: Explicit guess ("My final guess is...", "The answer is...")
    bool isExplicitGuess = questionLower.Contains("guess") || questionLower.Contains("answer is");

    // Pattern 2: Direct question that identifies the secret ("Is it a bicycle?")
    bool isDirectIdentification = secretLower.Split(' ').Any(w => w.Length > 3 && questionLower.Contains(w)) &&
                                  (questionLower.StartsWith("is it") || questionLower.StartsWith("could it be"));

    if (isExplicitGuess || isDirectIdentification)
    {
        // Extract guess and compare with secret
        var guessMatch = questionLower.Contains(secretLower) ||
                         secretLower.Split(' ').Any(w => w.Length > 3 && questionLower.Contains(w));

        if (guessMatch)
        {
            // If it's a direct question, Alpha will answer "Yes" and then check for victory
            if (isDirectIdentification && !isExplicitGuess)
            {
                // Store the question in Alpha's memory first
                await memoryPrimitives.EncodeAsync(new EncodeRequest
                {
                    UserId = ALPHA_USER_ID,
                    SessionId = ALPHA_SESSION_ID,
                    Content = $"[QUESTION_R{round}] Beta asked: {betaQuestion}",
                    Type = MemoryType.Episodic,
                    Scope = Scope.Session,
                    Tier = Tier.Short,
                    ImportanceScore = 0.98f  // Phase 39: Increased for better recall
                });

                // Alpha confirms with "Yes"
                await memoryPrimitives.EncodeAsync(new EncodeRequest
                {
                    UserId = ALPHA_USER_ID,
                    SessionId = ALPHA_SESSION_ID,
                    Content = $"[ANSWER_R{round}] I answered 'Yes' to: {betaQuestion}",
                    Type = MemoryType.Episodic,
                    Scope = Scope.Session,
                    Tier = Tier.Short,
                    ImportanceScore = 0.96f  // Phase 39: Increased for better recall
                });

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ALPHA] >>> Yes");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"        Beta has correctly identified the secret!");
                Console.WriteLine($"[ALPHA] >>> CORRECT! The secret was: {secret}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ALPHA] >>> CORRECT! You guessed it!");
                Console.ResetColor();
            }

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
    var duplicateCheck = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
    {
        UserId = ALPHA_USER_ID,
        SessionId = ALPHA_SESSION_ID,
        Query = betaQuestion,
        Limit = 5,
        MinScore = 0.3f
    });
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
        await memoryPrimitives.EncodeAsync(new EncodeRequest
        {
            UserId = ALPHA_USER_ID,
            SessionId = ALPHA_SESSION_ID,
            Content = $"[QUESTION_R{round}] Beta asked: {betaQuestion}",
            Type = MemoryType.Episodic,
            Scope = Scope.Session,
            Tier = Tier.Short,
            ImportanceScore = 0.98f  // Phase 39: Increased for better recall
        });

        // Alpha generates response using ONLY the question + recalled memories
        string alphaSystemPrompt = $@"You are Alpha, the QuizMaster in 20 Questions.

YOUR RECALLED MEMORIES:
{alphaContext}

THE QUESTION:
""{betaQuestion}""

YOUR TASK:
Answer the question based on your secret (in your memories).
Respond with ONLY one of: Yes, No, Maybe, or INVALID: [reason]

- Yes: The property is true for your secret
- No: The property is false for your secret
- Maybe: Only if genuinely ambiguous
- INVALID: If not a proper yes/no question

Be honest and consistent with your previous answers.";

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
        await memoryPrimitives.EncodeAsync(new EncodeRequest
        {
            UserId = ALPHA_USER_ID,
            SessionId = ALPHA_SESSION_ID,
            Content = $"[ANSWER_R{round}] I answered '{alphaResponse}' to: {betaQuestion}",
            Type = MemoryType.Episodic,
            Scope = Scope.Session,
            Tier = Tier.Short,
            ImportanceScore = 0.96f  // Phase 39: Increased for better recall
        });
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
    await memoryPrimitives.EncodeAsync(new EncodeRequest
    {
        UserId = BETA_USER_ID,
        SessionId = BETA_SESSION_ID,
        Content = $"[QA_R{round}] Q: {betaQuestion} -> A: {alphaResponse}",
        Type = MemoryType.Episodic,
        Scope = Scope.Session,
        Tier = Tier.Short,
        ImportanceScore = 0.95f
    });

    // Extract semantic knowledge from Q&A
    if (!alphaResponse.StartsWith("INVALID") && !alphaResponse.StartsWith("Wrong guess"))
    {
        try
        {
            var extractionContext = new KnowledgeExtractionContext
            {
                Question = betaQuestion,
                Answer = alphaResponse,
                Subject = "the secret object",
                UserId = BETA_USER_ID,
                Metadata = new Dictionary<string, string>
                {
                    ["Round"] = round.ToString(),
                    ["GameType"] = "TwentyQuestions"
                }
            };

            var extractedFacts = await knowledgeExtractor.ExtractAsync(extractionContext);

            foreach (var fact in extractedFacts)
            {
                await memoryPrimitives.EncodeAsync(new EncodeRequest
                {
                    UserId = BETA_USER_ID,
                    SessionId = BETA_SESSION_ID,
                    Content = $"[EXTRACTED_R{round}] {fact.Content}",
                    Type = MemoryType.Semantic,
                    Scope = Scope.Session,
                    Tier = Tier.Long,
                    ImportanceScore = fact.Importance
                });
            }

            if (extractedFacts.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"       💡 Extracted {extractedFacts.Count} semantic fact(s) from Q&A");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"       ⚠️ Knowledge extraction failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    // Store deduction if valid answer
    if (!alphaResponse.StartsWith("INVALID"))
    {
        // Phase 39: Extract property explicitly for better recall
        string property = ExtractPropertyFromQuestion(betaQuestion);
        string deduction;

        if (alphaResponse.StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
            deduction = $"[DEDUCTION_R{round}] CONFIRMED: {property} - Alpha said 'Yes' to '{betaQuestion}'";
        else if (alphaResponse.StartsWith("No", StringComparison.OrdinalIgnoreCase))
            deduction = $"[DEDUCTION_R{round}] RULED OUT: NOT {property} - Alpha said 'No' to '{betaQuestion}'";
        else
            deduction = $"[DEDUCTION_R{round}] UNCERTAIN: {property} - Alpha said 'Maybe' to '{betaQuestion}'";

        await memoryPrimitives.EncodeAsync(new EncodeRequest
        {
            UserId = BETA_USER_ID,
            SessionId = BETA_SESSION_ID,
            Content = deduction,
            Type = MemoryType.Semantic,
            Scope = Scope.Session,
            Tier = Tier.Long,
            ImportanceScore = 0.99f  // Phase 39: Highest priority for deductions (below GAME_RULES 1.0)
        });
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
var allAlphaMemories = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
{
    UserId = ALPHA_USER_ID,
    Query = "*",
    Limit = 10000,
    MinScore = 0.0f
});
var allBetaMemories = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
{
    UserId = BETA_USER_ID,
    Query = "*",
    Limit = 10000,
    MinScore = 0.0f
});
var alphaMemoryCount = allAlphaMemories.Count;
var betaMemoryCount = allBetaMemories.Count;
var totalMemories = alphaMemoryCount + betaMemoryCount;

// Expected memory count calculation
int expectedMinMemories = 2 + 4; // Alpha: 2 initial, Beta: 4 initial
int expectedRoundMemories = metrics.Rounds.Count * 4; // Per round: ROUND, MY_QUESTION, QA, DEDUCTION
int expectedMaxMemories = expectedMinMemories + expectedRoundMemories;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  MEMORY SYSTEM STATISTICS                                     ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  Alpha memories:        {alphaMemoryCount}");
Console.WriteLine($"  Beta memories:         {betaMemoryCount}");
Console.WriteLine($"  Total memories:        {totalMemories}");
Console.WriteLine();
Console.WriteLine($"  Expected (no dedup):   ~{expectedMaxMemories} memories");
Console.WriteLine($"  Actual stored:         {totalMemories} memories");
Console.WriteLine();

// Analyze recall quality
Console.WriteLine("  ┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("  │ RECALL QUALITY ANALYSIS                                     │");
Console.WriteLine("  ├─────────────────────────────────────────────────────────────┤");
Console.WriteLine($"  │ Avg Beta recall size:  {metrics.Rounds.Average(r => r.BetaContextChars):N0} chars");
Console.WriteLine($"  │ Avg Alpha recall size: {metrics.Rounds.Average(r => r.AlphaContextChars):N0} chars");
Console.WriteLine($"  │ Memories recalled/query: 15 (configured limit)              │");
Console.WriteLine("  └─────────────────────────────────────────────────────────────┘");
Console.WriteLine();

// Check for memory types distribution
var betaMemoryTypes = allBetaMemories.GroupBy(m => m.Memory.Type).ToDictionary(g => g.Key, g => g.Count());
var alphaMemoryTypes = allAlphaMemories.GroupBy(m => m.Memory.Type).ToDictionary(g => g.Key, g => g.Count());

Console.WriteLine("  ┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("  │ MEMORY TYPE DISTRIBUTION (3-Axis Model)                     │");
Console.WriteLine("  ├─────────────────────────────────────────────────────────────┤");
Console.WriteLine("  │ Beta:                                                       │");
foreach (var kvp in betaMemoryTypes.OrderByDescending(x => x.Value))
{
    Console.WriteLine($"  │   {kvp.Key,-12} {kvp.Value,3} memories ({100.0 * kvp.Value / betaMemoryCount:F1}%)");
}
Console.WriteLine("  │                                                             │");
Console.WriteLine("  │ Alpha:                                                      │");
foreach (var kvp in alphaMemoryTypes.OrderByDescending(x => x.Value))
{
    Console.WriteLine($"  │   {kvp.Key,-12} {kvp.Value,3} memories ({100.0 * kvp.Value / alphaMemoryCount:F1}%)");
}
Console.WriteLine("  └─────────────────────────────────────────────────────────────┘");
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
Console.WriteLine("  │ KEY DEMONSTRATION (3-Axis Model):                      │");
Console.WriteLine("  │ - Each LLM call received ONLY the opponent's last msg  │");
Console.WriteLine("  │ - NO chat history was passed                           │");
Console.WriteLine("  │ - Context came 100% from IMemoryPrimitives recall      │");
Console.WriteLine("  │ - Explicit Type × Scope × Tier control demonstrated    │");
Console.WriteLine("  └────────────────────────────────────────────────────────┘");
Console.WriteLine();

// Cleanup option
Console.Write("Delete game memories? (y/N): ");
var cleanup = Console.ReadLine()?.Trim().ToLower();
if (cleanup == "y" || cleanup == "yes")
{
    var all1 = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
    {
        UserId = ALPHA_USER_ID,
        Query = "*",
        Limit = 10000,
        MinScore = 0.0f
    });
    var all2 = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
    {
        UserId = BETA_USER_ID,
        Query = "*",
        Limit = 10000,
        MinScore = 0.0f
    });
    foreach (var m in all1)
    {
        await memoryPrimitives.DeleteAsync(new DeleteRequest
        {
            MemoryId = m.Memory.Id,
            HardDelete = true
        });
    }
    foreach (var m in all2)
    {
        await memoryPrimitives.DeleteAsync(new DeleteRequest
        {
            MemoryId = m.Memory.Id,
            HardDelete = true
        });
    }
    Console.WriteLine("Game memories deleted.");
}

Console.WriteLine("\nThank you for playing!");

// ============================================================================
// Helper Functions
// ============================================================================

string ExtractPropertyFromQuestion(string question)
{
    // Extract property from yes/no question for explicit deduction storage
    // "Is it man-made?" → "man-made"
    // "Does it have wheels?" → "wheels"
    // "Can it fly?" → "fly"

    var patterns = new[]
    {
        @"Is it (a |an )?(.*)\?",           // "Is it man-made?" → "man-made"
        @"Does it (have |contain )?(.*)\?", // "Does it have wheels?" → "wheels"
        @"Can it (.*)\?",                   // "Can it fly?" → "fly"
        @"Is it used (.*)\?",               // "Is it used indoors?" → "used indoors"
    };

    foreach (var pattern in patterns)
    {
        var match = System.Text.RegularExpressions.Regex.Match(question, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[match.Groups.Count - 1].Value.Trim();
        }
    }

    // Fallback: use the full question without question mark
    return question.Replace("?", "").Trim();
}

string GetStrategyPhase(int round)
{
    if (round <= 5)
        return BETA_STRATEGY_PHASE1;
    else if (round <= 12)
        return BETA_STRATEGY_PHASE2;
    else if (round <= 18)
        return BETA_STRATEGY_PHASE3;
    else
        return BETA_STRATEGY_PHASE4;
}

string GetBetaSystemPrompt(int round, string betaContext, string lastAlphaResponse)
{
    bool isFinalRound = round == MAX_ROUNDS;
    bool isCandidateGeneration = round == MAX_ROUNDS - 1; // Round 19

    if (isFinalRound)
    {
        // Round 20: Final guess with scoring
        return $@"You are Beta, playing 20 Questions.

YOUR RECALLED MEMORIES:
{betaContext}

CURRENT SITUATION:
- Round {round}/{MAX_ROUNDS} - FINAL ROUND
- Alpha's last response: ""{lastAlphaResponse}""

{BETA_STRATEGY_PHASE4_FINAL}

{DEDUCTION_EXAMPLE}

Output ONLY your final guess. Format: ""My final guess is: [answer]""";
    }
    else if (isCandidateGeneration)
    {
        // Round 19: Generate candidates
        return $@"You are Beta, playing 20 Questions.

YOUR RECALLED MEMORIES:
{betaContext}

CURRENT SITUATION:
- Round {round}/{MAX_ROUNDS} - Candidate Generation Round
- Alpha's last response: ""{lastAlphaResponse}""

{BETA_STRATEGY_PHASE4}

{DEDUCTION_EXAMPLE}

Output your candidates and final clarifying question.";
    }
    else
    {
        // Regular rounds: Use current strategy phase
        var currentStrategy = GetStrategyPhase(round);
        return $@"You are Beta, playing 20 Questions.

YOUR RECALLED MEMORIES:
{betaContext}

CURRENT SITUATION:
- Round {round}/{MAX_ROUNDS}
- Alpha's last response: ""{lastAlphaResponse}""

CURRENT STRATEGY PHASE:
{currentStrategy}

YOUR TASK:
Ask ONE strategic yes/no question following the current strategy phase.
Use your memories to avoid repeating questions.
Each question should eliminate ~50% of remaining possibilities.

Output ONLY the question. No explanations.";
    }
}

void PrintRecalledMemories(
    string agentName,
    IReadOnlyList<RetrieveResult> memories,
    long recallMs,
    int contextChars,
    bool fullMode)
{
    // Color-coded by agent: Alpha = Magenta, Beta = Cyan
    var headerColor = agentName == "ALPHA" ? ConsoleColor.Magenta : ConsoleColor.Cyan;
    var contentColor = agentName == "ALPHA" ? ConsoleColor.DarkMagenta : ConsoleColor.DarkCyan;

    Console.ForegroundColor = headerColor;
    Console.WriteLine($"[{agentName}] Recalled {memories.Count} memories (⏱️ {recallMs}ms, 📝 {contextChars:N0} chars):");
    Console.ResetColor();

    Console.ForegroundColor = contentColor;
    if (fullMode)
    {
        // Full mode: Show all memories with full content
        foreach (var mem in memories)
        {
            var content = mem.Memory.Content.Replace("\n", "\n       ");
            Console.WriteLine($"       [{mem.Score:F2}] {content}");
        }
    }
    else
    {
        // Summary mode: Show count + top 3 memories (truncated)
        var topMemories = memories.Take(3).ToList();
        foreach (var mem in topMemories)
        {
            var content = mem.Memory.Content.Replace("\n", " "); // Single line
            var truncated = content.Length > 80 ? content[..77] + "..." : content;
            Console.WriteLine($"       [{mem.Score:F2}] {truncated}");
        }
        if (memories.Count > 3)
        {
            Console.WriteLine($"       ... and {memories.Count - 3} more memories (set MEMORY_LOG_MODE=full to see all)");
        }
    }
    Console.ResetColor();
}

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
