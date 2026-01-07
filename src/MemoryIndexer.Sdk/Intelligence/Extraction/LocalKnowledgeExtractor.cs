using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Extraction;

/// <summary>
/// Rule-based knowledge extractor using pattern matching on Q&A exchanges.
/// Phase 25: Semantic Knowledge Extraction.
/// </summary>
/// <remarks>
/// Extracts factual knowledge from conversational Q&A pairs to generate
/// Semantic memories, addressing memory type imbalance.
///
/// Supported patterns:
/// - "Is it X?" + Yes/No/Maybe
/// - "Does it have X?" + Yes/No
/// - "Can it X?" + Yes/No
/// - "Is it a/an X?" + Yes/No
/// </remarks>
public sealed partial class LocalKnowledgeExtractor : IKnowledgeExtractor
{
    private readonly ILogger<LocalKnowledgeExtractor> _logger;

    public LocalKnowledgeExtractor(ILogger<LocalKnowledgeExtractor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ExtractedFact>> ExtractAsync(
        KnowledgeExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        var facts = new List<ExtractedFact>();

        var question = context.Question.Trim();
        var answer = context.Answer.Trim().ToLowerInvariant();
        var subject = context.Subject ?? "it";

        // Normalize answer to canonical form
        var answerType = NormalizeAnswer(answer);
        if (answerType == AnswerType.Unknown)
        {
            _logger.LogDebug("Unknown answer type: {Answer}", answer);
            return Task.FromResult<IReadOnlyList<ExtractedFact>>(facts);
        }

        // Try each pattern in order (most specific first)
        var extracted = TryExtractIsItA(question, answerType, subject)
            ?? TryExtractIsIt(question, answerType, subject)
            ?? TryExtractDoesItHave(question, answerType, subject)
            ?? TryExtractCanIt(question, answerType, subject);

        if (extracted != null)
        {
            facts.Add(extracted);
            _logger.LogDebug(
                "Extracted fact: {Content} (confidence={Confidence:F2})",
                extracted.Content,
                extracted.Confidence);
        }

        return Task.FromResult<IReadOnlyList<ExtractedFact>>(facts);
    }

    #region Pattern Matching

    /// <summary>
    /// Pattern: "Is it {property}?" → Extract property assertion
    /// </summary>
    private ExtractedFact? TryExtractIsIt(string question, AnswerType answer, string subject)
    {
        // Pattern: "Is it X?" or "Is it X (...)?"
        var match = IsItPattern().Match(question);
        if (!match.Success)
        {
            return null;
        }

        var property = match.Groups[1].Value.Trim();

        return answer switch
        {
            AnswerType.Yes => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} is {property}",
                Confidence = 0.8f,
                Importance = 0.7f,
                Source = "Pattern:IsIt_Yes"
            },
            AnswerType.No => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} is not {property}",
                Confidence = 0.9f,  // Negations are usually more certain
                Importance = 0.6f,
                Source = "Pattern:IsIt_No"
            },
            AnswerType.Maybe => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} may be {property}",
                Confidence = 0.5f,  // Lower confidence for uncertain answers
                Importance = 0.5f,
                Source = "Pattern:IsIt_Maybe"
            },
            _ => null
        };
    }

    /// <summary>
    /// Pattern: "Is it a/an {noun}?" → Extract category assertion
    /// </summary>
    private ExtractedFact? TryExtractIsItA(string question, AnswerType answer, string subject)
    {
        // Pattern: "Is it a X?" or "Is it an X?"
        var match = IsItAPattern().Match(question);
        if (!match.Success)
        {
            return null;
        }

        var article = match.Groups[1].Value;  // "a" or "an"
        var noun = match.Groups[2].Value.Trim();

        return answer switch
        {
            AnswerType.Yes => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} is {article} {noun}",
                Confidence = 0.85f,
                Importance = 0.75f,  // Category assertions are important
                Source = "Pattern:IsItA_Yes"
            },
            AnswerType.No => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} is not {article} {noun}",
                Confidence = 0.9f,
                Importance = 0.7f,
                Source = "Pattern:IsItA_No"
            },
            AnswerType.Maybe => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} may be {article} {noun}",
                Confidence = 0.5f,
                Importance = 0.6f,
                Source = "Pattern:IsItA_Maybe"
            },
            _ => null
        };
    }

    /// <summary>
    /// Pattern: "Does it have {property}?" → Extract possession/feature
    /// </summary>
    private ExtractedFact? TryExtractDoesItHave(string question, AnswerType answer, string subject)
    {
        // Pattern: "Does it have X?" or "Does it have X (...)?"
        var match = DoesItHavePattern().Match(question);
        if (!match.Success)
        {
            return null;
        }

        var property = match.Groups[1].Value.Trim();

        return answer switch
        {
            AnswerType.Yes => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} has {property}",
                Confidence = 0.8f,
                Importance = 0.65f,
                Source = "Pattern:DoesItHave_Yes"
            },
            AnswerType.No => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} does not have {property}",
                Confidence = 0.85f,
                Importance = 0.6f,
                Source = "Pattern:DoesItHave_No"
            },
            AnswerType.Maybe => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} may have {property}",
                Confidence = 0.5f,
                Importance = 0.5f,
                Source = "Pattern:DoesItHave_Maybe"
            },
            _ => null
        };
    }

    /// <summary>
    /// Pattern: "Can it {action}?" → Extract capability
    /// </summary>
    private ExtractedFact? TryExtractCanIt(string question, AnswerType answer, string subject)
    {
        // Pattern: "Can it X?" or "Can you X it?"
        var match = CanItPattern().Match(question);
        if (!match.Success)
        {
            return null;
        }

        var action = match.Groups[1].Value.Trim();

        return answer switch
        {
            AnswerType.Yes => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} can {action}",
                Confidence = 0.75f,
                Importance = 0.6f,
                Source = "Pattern:CanIt_Yes"
            },
            AnswerType.No => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} cannot {action}",
                Confidence = 0.8f,
                Importance = 0.65f,
                Source = "Pattern:CanIt_No"
            },
            AnswerType.Maybe => new ExtractedFact
            {
                Content = $"{CapitalizeFirst(subject)} may be able to {action}",
                Confidence = 0.5f,
                Importance = 0.5f,
                Source = "Pattern:CanIt_Maybe"
            },
            _ => null
        };
    }

    #endregion

    #region Helper Methods

    private static AnswerType NormalizeAnswer(string answer)
    {
        return answer switch
        {
            "yes" or "y" or "true" => AnswerType.Yes,
            "no" or "n" or "false" => AnswerType.No,
            "maybe" or "m" or "uncertain" or "not sure" => AnswerType.Maybe,
            _ => AnswerType.Unknown
        };
    }

    private static string CapitalizeFirst(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private enum AnswerType
    {
        Unknown,
        Yes,
        No,
        Maybe
    }

    #endregion

    #region Regex Patterns

    /// <summary>
    /// Matches: "Is it X?" or "Is it X (...)?"
    /// Captures: property X
    /// </summary>
    [GeneratedRegex(@"^Is it (.+?)(?:\s*\(|$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex IsItPattern();

    /// <summary>
    /// Matches: "Is it a/an X?" or "Is it a/an X (...)?"
    /// Captures: article (a/an), noun X
    /// </summary>
    [GeneratedRegex(@"^Is it (a|an) (.+?)(?:\s*\(|$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex IsItAPattern();

    /// <summary>
    /// Matches: "Does it have X?" or "Does it have X (...)?"
    /// Captures: property X
    /// </summary>
    [GeneratedRegex(@"^Does it have (.+?)(?:\s*\(|$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex DoesItHavePattern();

    /// <summary>
    /// Matches: "Can it X?" or "Can you X it?"
    /// Captures: action X
    /// </summary>
    [GeneratedRegex(@"^Can (?:it|you) (.+?)(?:\s*\(|$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex CanItPattern();

    #endregion
}
