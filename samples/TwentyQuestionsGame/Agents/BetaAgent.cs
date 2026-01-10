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

    private const int MaxRetryAttempts = 2;

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
        // Memory Indexer Validation: Beta must rely on memory_recall for Q&A history
        // No direct injection - this tests semantic search functionality
        // ==========================================================================

        var userMessageBuilder = new System.Text.StringBuilder();
        userMessageBuilder.Append($"Alpha says: \"{alphaLastResponse}\"\n\n");

        // NOTE: Q&A history is NOT injected here - Beta must use memory_recall
        // This validates Memory Indexer's recall functionality

        if (currentRound >= 19)
        {
            userMessageBuilder.Append($"⚠️ THIS IS ROUND {currentRound}/20 - YOU MUST MAKE YOUR FINAL GUESS NOW!\nFormat: \"My final guess is: [specific answer]\"");
        }
        else
        {
            userMessageBuilder.Append("Use memory_recall to check previous Q&A, then ask your next question.");
        }

        var userMessage = userMessageBuilder.ToString();

        AgentResponse response;
        string question;
        var attempt = 0;

        do
        {
            attempt++;
            response = await ProcessWithToolsAsync(systemPrompt, userMessage, ct);
            question = CleanQuestion(response.FinalOutput);

            // If empty question and not last attempt, add stronger instruction
            if (string.IsNullOrEmpty(question) && attempt < MaxRetryAttempts)
            {
                userMessage = $"{userMessage}\n\n⚠️ YOUR PREVIOUS RESPONSE DID NOT CONTAIN A VALID QUESTION. You MUST output a yes/no question ending with '?' or a final guess starting with 'My final guess is:'";
                logger.LogWarning("Beta failed to generate question (attempt {Attempt}), retrying...", attempt);
            }
        } while (string.IsNullOrEmpty(question) && attempt < MaxRetryAttempts);

        // If still no question after retries, generate a fallback based on context
        if (string.IsNullOrEmpty(question))
        {
            question = GenerateContextualFallback(questionHistory, currentRound);
            logger.LogWarning("Beta failed to generate question after {Attempts} attempts, using fallback: {Fallback}", attempt, question);
        }

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
            MemoryRecallMs = response.MemoryRecallMs,
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

        // If question is empty or just punctuation, return empty - let caller detect failure
        // DO NOT provide hardcoded fallback - this caused duplicate question bugs!
        if (string.IsNullOrWhiteSpace(question) || question.Length < 3)
        {
            return ""; // Empty signals failure, caller should retry or handle
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
    // Phase 48: Restored Duplicate Detection + Contextual Fallback
    // ==========================================================================
    // Phase 62 removed hardcoding but caused duplicate question bugs.
    // Restoring Jaccard similarity for duplicate detection as safety net.
    // ==========================================================================

    /// <summary>
    /// Generates a contextual fallback question when LLM fails to produce one.
    /// Uses question history to pick an unanswered category question.
    /// </summary>
    private static string GenerateContextualFallback(
        IReadOnlyList<(string Question, string Answer)>? questionHistory,
        int currentRound)
    {
        // Standard category questions in order of usefulness
        var categoryQuestions = new[]
        {
            "Is it a living thing?",
            "Is it man-made?",
            "Is it a physical object?",
            "Is it larger than a car?",
            "Is it found indoors?",
            "Is it located in Europe?",
            "Is it located in Asia?",
            "Is it something famous?",
            "Is it used for entertainment?",
            "Is it edible?"
        };

        if (questionHistory == null || questionHistory.Count == 0)
        {
            return categoryQuestions[0];
        }

        // Find a question that hasn't been asked yet
        var askedQuestions = questionHistory.Select(h => NormalizeQuestion(h.Question)).ToHashSet();

        foreach (var q in categoryQuestions)
        {
            if (!askedQuestions.Contains(NormalizeQuestion(q)))
            {
                return q;
            }
        }

        // If all category questions asked, generate based on round
        return currentRound >= 15
            ? "Is it something that can be visited by tourists?"
            : "Is it something that exists in nature?";
    }

    /// <summary>
    /// Check for duplicate questions using Jaccard similarity.
    /// Restored from Phase 62 experiment - LLM-only duplicate detection was unreliable.
    /// </summary>
    private static (bool IsDuplicate, int? OriginalRound, float SimilarityScore) CheckForDuplicate(
        string currentQuestion,
        IReadOnlyList<(string Question, string Answer)>? questionHistory)
    {
        if (questionHistory == null || questionHistory.Count == 0)
        {
            return (false, null, 0f);
        }

        var currentNormalized = NormalizeQuestion(currentQuestion);
        var currentWords = currentNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var bestMatch = (IsDuplicate: false, Round: (int?)null, Score: 0f);

        for (int i = 0; i < questionHistory.Count; i++)
        {
            var prevNormalized = NormalizeQuestion(questionHistory[i].Question);
            var prevWords = prevNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            // Jaccard similarity
            var intersection = currentWords.Intersect(prevWords).Count();
            var union = currentWords.Union(prevWords).Count();
            var similarity = union > 0 ? (float)intersection / union : 0f;

            if (similarity > bestMatch.Score)
            {
                bestMatch = (similarity >= 0.7f, i + 1, similarity); // Round is 1-indexed
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Normalizes a question for comparison.
    /// </summary>
    private static string NormalizeQuestion(string question)
    {
        // Remove punctuation, lowercase, remove common words
        var normalized = question.ToLowerInvariant()
            .Replace("?", "")
            .Replace("!", "")
            .Replace(".", "")
            .Replace(",", "")
            .Trim();

        // Remove common question prefixes
        var prefixes = new[] { "is it ", "are there ", "does it ", "can it ", "will it ", "has it ", "my final guess is " };
        foreach (var prefix in prefixes)
        {
            if (normalized.StartsWith(prefix))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        return normalized;
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
    public long MemoryRecallMs { get; init; }
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
