using System.Text.RegularExpressions;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Scoring;

/// <summary>
/// Analyzes content to determine importance score.
/// Uses heuristics and patterns to estimate importance without LLM.
/// Can be extended to use LLM-based scoring.
/// </summary>
public sealed partial class ImportanceAnalyzer
{
    private readonly ILogger<ImportanceAnalyzer> _logger;

    // High importance indicators
    private static readonly string[] HighImportanceKeywords =
    [
        "important", "critical", "urgent", "remember", "never forget",
        "always", "must", "essential", "key", "priority",
        "deadline", "requirement", "decision", "agreement", "promise",
        "password", "secret", "credential", "api key", "token"
    ];

    // Medium importance indicators
    private static readonly string[] MediumImportanceKeywords =
    [
        "note", "fyi", "update", "change", "prefer", "like",
        "want", "need", "should", "would", "plan", "goal",
        "project", "task", "meeting", "schedule"
    ];

    // Low importance indicators (common/generic)
    private static readonly string[] LowImportanceKeywords =
    [
        "hello", "hi", "thanks", "thank you", "okay", "ok",
        "sure", "yes", "no", "maybe", "test", "example"
    ];

    public ImportanceAnalyzer(ILogger<ImportanceAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes content and returns an importance score (0.0 to 1.0).
    /// </summary>
    public float AnalyzeImportance(string content, MemoryType memoryType = MemoryType.Episodic)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.1f;

        var normalizedContent = content.ToLowerInvariant();
        var score = 0.5f; // Base score

        // Factor 1: Keyword analysis
        score += AnalyzeKeywords(normalizedContent);

        // Factor 2: Content structure
        score += AnalyzeStructure(normalizedContent, content);

        // Factor 3: Memory type bonus
        score += GetMemoryTypeBonus(memoryType);

        // Factor 4: Named entity presence
        score += AnalyzeNamedEntities(content);

        // Factor 5: Specificity (numbers, dates, names)
        score += AnalyzeSpecificity(content);

        // Clamp to valid range
        var finalScore = Math.Clamp(score, 0.1f, 1.0f);

        _logger.LogDebug(
            "Importance analysis: length={Length}, type={Type}, score={Score:F2}",
            content.Length, memoryType, finalScore);

        return finalScore;
    }

    /// <summary>
    /// Batch analyzes multiple contents.
    /// </summary>
    public IReadOnlyList<float> AnalyzeBatch(
        IEnumerable<string> contents,
        MemoryType memoryType = MemoryType.Episodic)
    {
        return contents.Select(c => AnalyzeImportance(c, memoryType)).ToList();
    }

    private static float AnalyzeKeywords(string content)
    {
        var score = 0f;

        // High importance keywords
        foreach (var keyword in HighImportanceKeywords)
        {
            if (content.Contains(keyword))
            {
                score += 0.15f;
            }
        }

        // Medium importance keywords
        foreach (var keyword in MediumImportanceKeywords)
        {
            if (content.Contains(keyword))
            {
                score += 0.05f;
            }
        }

        // Low importance keywords reduce score slightly
        foreach (var keyword in LowImportanceKeywords)
        {
            if (content.Contains(keyword))
            {
                score -= 0.02f;
            }
        }

        return Math.Clamp(score, -0.2f, 0.3f);
    }

    private static float AnalyzeStructure(string normalizedContent, string originalContent)
    {
        var score = 0f;

        // Length bonus (longer = potentially more important, up to a point)
        var wordCount = normalizedContent.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount >= 10 && wordCount <= 100)
        {
            score += 0.05f;
        }
        else if (wordCount > 100)
        {
            score += 0.1f;
        }

        // Bullet points or numbered lists indicate structured information
        if (BulletPointRegex().IsMatch(originalContent))
        {
            score += 0.1f;
        }

        // Questions might indicate important inquiries
        if (originalContent.Contains('?'))
        {
            score += 0.05f;
        }

        // Exclamations might indicate urgency
        if (originalContent.Contains('!'))
        {
            score += 0.03f;
        }

        return score;
    }

    private static float GetMemoryTypeBonus(MemoryType memoryType)
    {
        return memoryType switch
        {
            MemoryType.Procedural => 0.15f,  // How-to knowledge is valuable
            MemoryType.Fact => 0.1f,          // Facts are generally important
            MemoryType.Semantic => 0.05f,     // Concepts are moderately important
            MemoryType.Episodic => 0f,        // Events are baseline
            _ => 0f
        };
    }

    private static float AnalyzeNamedEntities(string content)
    {
        var score = 0f;

        // Capitalized words (potential names/entities)
        var capitalizedWords = CapitalWordRegex().Matches(content).Count;
        if (capitalizedWords > 2)
        {
            score += Math.Min(0.1f, capitalizedWords * 0.02f);
        }

        // Email addresses
        if (EmailRegex().IsMatch(content))
        {
            score += 0.1f;
        }

        // URLs
        if (UrlRegex().IsMatch(content))
        {
            score += 0.05f;
        }

        return score;
    }

    private static float AnalyzeSpecificity(string content)
    {
        var score = 0f;

        // Numbers (specific information)
        var numberCount = NumberRegex().Matches(content).Count;
        if (numberCount > 0)
        {
            score += Math.Min(0.1f, numberCount * 0.03f);
        }

        // Dates
        if (DateRegex().IsMatch(content))
        {
            score += 0.1f;
        }

        // Time
        if (TimeRegex().IsMatch(content))
        {
            score += 0.05f;
        }

        // Currency
        if (CurrencyRegex().IsMatch(content))
        {
            score += 0.1f;
        }

        return score;
    }

    [GeneratedRegex(@"^[\s]*[-*•]\s", RegexOptions.Multiline)]
    private static partial Regex BulletPointRegex();

    [GeneratedRegex(@"\b[A-Z][a-z]+\b")]
    private static partial Regex CapitalWordRegex();

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"https?://[^\s]+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b|\b\d{4}[/-]\d{1,2}[/-]\d{1,2}\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"[$€£¥₩]\d+|\d+[$€£¥₩]")]
    private static partial Regex CurrencyRegex();
}
