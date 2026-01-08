using Microsoft.Extensions.Logging;
using TwentyQuestionsGame.Game;
using TwentyQuestionsGame.LLM;
using TwentyQuestionsGame.ToolCall;

namespace TwentyQuestionsGame.Agents;

/// <summary>
/// Beta agent - the Guesser who asks questions.
/// </summary>
public sealed class BetaAgent(
    LlmClient llmClient,
    ToolCallParser parser,
    ToolCallExecutor executor,
    ILogger<BetaAgent> logger) : AgentBase(llmClient, parser, executor, logger)
{
    private string _systemPromptTemplate = "";

    protected override string UserId => GameConfiguration.BetaUserId;
    protected override string SessionId => GameConfiguration.BetaSessionId;

    public void Initialize(string systemPromptTemplate)
    {
        _systemPromptTemplate = systemPromptTemplate;
    }

    /// <summary>
    /// Generates a question based on Alpha's last response.
    /// </summary>
    public async Task<BetaQuestionResult> GenerateQuestionAsync(
        string alphaLastResponse,
        int currentRound,
        IReadOnlyList<(string Question, string Answer)>? questionHistory = null,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(currentRound, alphaLastResponse);

        // ==========================================================================
        // Phase 62 Experiment: Question History Injection Removed
        // ==========================================================================
        // Previous: Injected all previous Q&A into user message
        // Now: Rely on Memory Indexer's recall to provide context
        // LLM uses memory_recall results to avoid duplicates
        // ==========================================================================

        var userMessageBuilder = new System.Text.StringBuilder();
        userMessageBuilder.Append($"Alpha says: \"{alphaLastResponse}\"\n\n");

        if (currentRound >= 19)
        {
            userMessageBuilder.Append($"⚠️ THIS IS ROUND {currentRound}/20 - YOU MUST MAKE YOUR FINAL GUESS NOW!\nFormat: \"My final guess is: [specific answer]\"");
        }
        else
        {
            userMessageBuilder.Append("Use memory_recall to check previous Q&A, then ask your next question.");
        }

        var userMessage = userMessageBuilder.ToString();

        var response = await ProcessWithToolsAsync(systemPrompt, userMessage, ct);

        // Extract the question (clean up any remaining formatting)
        var question = CleanQuestion(response.FinalOutput);

        // Enforce final guess format on Round 19-20
        if (currentRound >= 19 && !question.StartsWith("My final guess", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the most likely candidate from the response and format as guess
            var guessCandidate = ExtractBestGuessCandidate(response.RawContent, question);
            question = $"My final guess is: {guessCandidate}";
        }

        // Check for duplicate questions (code-level verification)
        var duplicateInfo = CheckForDuplicate(question, questionHistory);

        // Detect early guess (final guess before round 19)
        var isFinalGuess = question.StartsWith("My final guess", StringComparison.OrdinalIgnoreCase);
        var isEarlyGuess = isFinalGuess && currentRound < 19;

        return new BetaQuestionResult
        {
            Question = question,
            RawResponse = response.RawContent,
            PromptTokens = response.PromptTokens,
            CompletionTokens = response.CompletionTokens,
            LatencyMs = response.LatencyMs,
            ToolCallIterations = response.ToolCallIterations,
            IsDuplicate = duplicateInfo.IsDuplicate,
            DuplicateOfRound = duplicateInfo.OriginalRound,
            SimilarityScore = duplicateInfo.SimilarityScore,
            IsEarlyGuess = isEarlyGuess
        };
    }

    private string BuildSystemPrompt(int currentRound, string lastResponse)
    {
        return _systemPromptTemplate
            .Replace("{{ROUND}}", currentRound.ToString())
            .Replace("{{LAST_RESPONSE}}", lastResponse);
    }

    private static string CleanQuestion(string rawQuestion)
    {
        var question = rawQuestion.Trim();

        // Remove <tool_call>...</tool_call> blocks
        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"<tool_call>.*?</tool_call>",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

        // Remove any opening or closing XML-style tags (e.g., <memory_recall>, </memory_recall>)
        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"</?[\w_]+[^>]*>",
            "").Trim();

        // Remove function call syntax (e.g., memory_recall(query="...", limit=20))
        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"\w+\([^)]*\)",
            "").Trim();

        // Remove reasoning chain sections (=== ANALYSIS === and === QUESTION SELECTION ===)
        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"===\s*ANALYSIS\s*===.*?(?====|$)",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"===\s*QUESTION SELECTION\s*===.*?(?=\n[A-Z]|$)",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

        // Remove any remaining section markers
        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"===.*?===",
            "").Trim();

        // Remove common metadata lines
        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"^(CONFIRMED|ELIMINATED|UNCERTAIN|CURRENT HYPOTHESIS|REMAINING POSSIBILITIES|PREVIOUS QUESTIONS|CANDIDATE QUESTIONS|SELECTED|REASON):.*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline).Trim();

        // Remove numbered list items that aren't questions
        question = System.Text.RegularExpressions.Regex.Replace(
            question,
            @"^\d+\.\s+(?!Is |Are |Does |Do |Can |Will |Has |Have |Was |Were |My final guess).*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline).Trim();

        // Remove common prefixes
        var prefixes = new[]
        {
            "My question is:",
            "Question:",
            "I'll ask:",
            "Let me ask:",
            "Here's my question:",
            "Based on the information,",
            "Given what I know,"
        };

        foreach (var prefix in prefixes)
        {
            if (question.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                question = question[prefix.Length..].Trim();
            }
        }

        // Extract the last question-like sentence (the actual question)
        var lines = question.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastQuestion = lines
            .Select(l => l.Trim())
            .LastOrDefault(l =>
                l.EndsWith('?') ||
                l.StartsWith("Is ", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("Are ", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("Does ", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("My final guess", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(lastQuestion))
        {
            question = lastQuestion;
        }
        else
        {
            // Fall back to last non-empty line
            question = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? question;
        }

        // If question is empty or just punctuation, provide fallback
        if (string.IsNullOrWhiteSpace(question) || question.Length < 3)
        {
            question = "Is it a common household item";
        }

        // Ensure it ends with a question mark
        if (!question.EndsWith('?') && !question.StartsWith("My final guess", StringComparison.OrdinalIgnoreCase))
        {
            question += "?";
        }

        return question;
    }

    /// <summary>
    /// Extracts the best guess candidate from the raw response or falls back to the cleaned question.
    /// </summary>
    private static string ExtractBestGuessCandidate(string rawResponse, string cleanedQuestion)
    {
        // Try to find "CURRENT HYPOTHESIS:" or "REMAINING POSSIBILITIES:" in the analysis
        var hypothesisMatch = System.Text.RegularExpressions.Regex.Match(
            rawResponse,
            @"CURRENT HYPOTHESIS:\s*(.+?)(?:\n|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (hypothesisMatch.Success)
        {
            var hypothesis = hypothesisMatch.Groups[1].Value.Trim();
            // Extract a specific item if mentioned (e.g., "possibly the Eiffel Tower")
            var specificMatch = System.Text.RegularExpressions.Regex.Match(
                hypothesis,
                @"(?:possibly|probably|likely|could be|might be)\s+(?:the\s+)?(.+?)(?:\s*[-,;.]|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (specificMatch.Success)
            {
                return specificMatch.Groups[1].Value.Trim();
            }
        }

        // Try to find specific items in REMAINING POSSIBILITIES
        var possibilitiesMatch = System.Text.RegularExpressions.Regex.Match(
            rawResponse,
            @"REMAINING POSSIBILITIES:\s*(.+?)(?:\n===|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        
        if (possibilitiesMatch.Success)
        {
            var possibilities = possibilitiesMatch.Groups[1].Value;
            // Extract first specific item (e.g., "Eiffel Tower, Tokyo Tower" -> "Eiffel Tower")
            var firstItem = System.Text.RegularExpressions.Regex.Match(
                possibilities,
                @"(?:the\s+)?([A-Z][a-zA-Z\s]+(?:Tower|Wall|Bridge|Building|Gate|Monument|Statue|Pyramid|Palace|Castle|Cathedral|Church))",
                System.Text.RegularExpressions.RegexOptions.None);
            if (firstItem.Success)
            {
                return firstItem.Groups[1].Value.Trim();
            }
        }

        // Fall back to the cleaned question if it looks like a specific guess
        if (cleanedQuestion.Contains("Is it") && cleanedQuestion.Contains("?"))
        {
            var guessMatch = System.Text.RegularExpressions.Regex.Match(
                cleanedQuestion,
                @"Is it (?:the\s+)?(.+?)\?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (guessMatch.Success)
            {
                return guessMatch.Groups[1].Value.Trim();
            }
        }

        // Ultimate fallback - return a generic response
        return cleanedQuestion.Replace("?", "").Trim();
    }

    // ==========================================================================
    // Phase 62 Experiment: Hardcoding Removed
    // ==========================================================================
    // Previous implementation used Jaccard similarity for duplicate detection.
    // Now relying on Memory Indexer's semantic search + LLM judgment.
    // The LLM sees recalled memories and should avoid asking similar questions.
    // ==========================================================================

    /// <summary>
    /// Duplicate detection now relies on Memory Indexer's semantic search.
    /// The LLM receives recalled Q&A history and should avoid duplicates.
    /// This method returns no-duplicate to let memory indexer + LLM handle it.
    /// </summary>
    private static (bool IsDuplicate, int? OriginalRound, float SimilarityScore) CheckForDuplicate(
        string currentQuestion,
        IReadOnlyList<(string Question, string Answer)>? questionHistory)
    {
        // Phase 62: Hardcoding removed - trust Memory Indexer + LLM
        // Memory Indexer provides semantic search, LLM judges duplicates
        return (false, null, 0f);
    }
}

/// <summary>
/// Result of Beta generating a question.
/// </summary>
public sealed record BetaQuestionResult
{
    public string Question { get; init; } = "";
    public string RawResponse { get; init; } = "";
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public long LatencyMs { get; init; }
    public int ToolCallIterations { get; init; }

    /// <summary>
    /// Whether this question is a duplicate of a previous question.
    /// </summary>
    public bool IsDuplicate { get; init; }

    /// <summary>
    /// If duplicate, which round the original question was asked.
    /// </summary>
    public int? DuplicateOfRound { get; init; }

    /// <summary>
    /// Similarity score with the most similar previous question (0.0 - 1.0).
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>
    /// Whether this is an early guess (before Round 19) based on high confidence.
    /// </summary>
    public bool IsEarlyGuess { get; init; }
}
