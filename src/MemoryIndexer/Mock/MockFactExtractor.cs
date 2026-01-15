using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

namespace MemoryIndexer.Mock;

/// <summary>
/// Mock implementation of IFactExtractor for testing.
/// Uses simple pattern matching for basic fact extraction.
/// </summary>
public sealed partial class MockFactExtractor : IFactExtractor
{
    /// <inheritdoc />
    public Task<FactExtractionResult> ExtractAsync(
        FactExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        var facts = new List<UserFact>();
        var content = context.Content;
        var contextType = DetectContextType(content);

        // Skip extraction for quoted or fictional content
        if (contextType is StatementContextType.Quoted or
            StatementContextType.Narrative or
            StatementContextType.RolePlay)
        {
            return Task.FromResult(new FactExtractionResult
            {
                Facts = [],
                OverallConfidence = 0.3f,
                ContextType = contextType,
                Reasoning = "Content detected as non-direct statement"
            });
        }

        // Pattern: "My name is X" / "내 이름은 X"
        var nameMatch = NamePattern().Match(content);
        if (nameMatch.Success)
        {
            var name = nameMatch.Groups[1].Value.Trim();
            facts.Add(new UserFact
            {
                Content = $"User's name is {name}",
                Category = FactCategory.Identity,
                Confidence = 0.95f,
                Importance = 0.9f,
                PromotionPath = PromotionPath.FastTrack,
                Subject = "user",
                Predicate = "name",
                Object = name,
                Entities = [name],
                Source = "pattern:name"
            });
        }

        // Pattern: "I am X years old" / "나는 X살"
        var ageMatch = AgePattern().Match(content);
        if (ageMatch.Success)
        {
            var age = ageMatch.Groups[1].Value;
            facts.Add(new UserFact
            {
                Content = $"User is {age} years old",
                Category = FactCategory.Identity,
                Confidence = 0.90f,
                Importance = 0.7f,
                PromotionPath = PromotionPath.FastTrack,
                Subject = "user",
                Predicate = "age",
                Object = age,
                Source = "pattern:age"
            });
        }

        // Pattern: "I like X" / "I love X" / "나는 X를 좋아해"
        var likeMatch = PreferencePattern().Match(content);
        if (likeMatch.Success)
        {
            var preference = likeMatch.Groups[1].Value.Trim();
            facts.Add(new UserFact
            {
                Content = $"User likes {preference}",
                Category = FactCategory.Preference,
                Confidence = 0.80f,
                Importance = 0.6f,
                PromotionPath = PromotionPath.Standard,
                Subject = "user",
                Predicate = "likes",
                Object = preference,
                Source = "pattern:preference"
            });
        }

        // Pattern: "I work as X" / "I am a X"
        var occupationMatch = OccupationPattern().Match(content);
        if (occupationMatch.Success)
        {
            var occupation = occupationMatch.Groups[1].Value.Trim();
            facts.Add(new UserFact
            {
                Content = $"User works as {occupation}",
                Category = FactCategory.Professional,
                Confidence = 0.85f,
                Importance = 0.7f,
                PromotionPath = PromotionPath.Standard,
                Subject = "user",
                Predicate = "occupation",
                Object = occupation,
                Source = "pattern:occupation"
            });
        }

        // Pattern: "I live in X"
        var locationMatch = LocationPattern().Match(content);
        if (locationMatch.Success)
        {
            var location = locationMatch.Groups[1].Value.Trim();
            facts.Add(new UserFact
            {
                Content = $"User lives in {location}",
                Category = FactCategory.Location,
                Confidence = 0.85f,
                Importance = 0.6f,
                PromotionPath = PromotionPath.Standard,
                Subject = "user",
                Predicate = "lives in",
                Object = location,
                Source = "pattern:location"
            });
        }

        var overallConfidence = facts.Count > 0
            ? facts.Average(f => f.Confidence)
            : 0f;

        return Task.FromResult(new FactExtractionResult
        {
            Facts = facts,
            OverallConfidence = overallConfidence,
            ContextType = contextType,
            Reasoning = $"Extracted {facts.Count} facts using pattern matching"
        });
    }

    /// <inheritdoc />
    public Task<FactValidationResult> ValidateAsync(
        UserFact fact,
        IReadOnlyList<MemoryUnit> existingFacts,
        CancellationToken cancellationToken = default)
    {
        // Simple duplicate check by content similarity
        foreach (var existing in existingFacts)
        {
            // Check for exact duplicate
            if (existing.Content.Equals(fact.Content, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new FactValidationResult
                {
                    IsValid = false,
                    ConflictType = FactConflictType.Duplicate,
                    ConflictingMemory = existing,
                    RecommendedAction = FactConflictAction.Skip,
                    Confidence = 0.95f,
                    Reasoning = "Exact duplicate found"
                });
            }

            // Check for same subject/predicate (potential update)
            if (!string.IsNullOrEmpty(fact.Subject) && !string.IsNullOrEmpty(fact.Predicate))
            {
                var subjectMatch = existing.Content.Contains(fact.Subject, StringComparison.OrdinalIgnoreCase);
                var predicateMatch = existing.Content.Contains(fact.Predicate, StringComparison.OrdinalIgnoreCase);

                if (subjectMatch && predicateMatch)
                {
                    // Same attribute, different value → ValueUpdate
                    return Task.FromResult(new FactValidationResult
                    {
                        IsValid = true,
                        ConflictType = FactConflictType.ValueUpdate,
                        ConflictingMemory = existing,
                        RecommendedAction = fact.Confidence >= existing.Confidence
                            ? FactConflictAction.Replace
                            : FactConflictAction.RequireReview,
                        Confidence = 0.8f,
                        Reasoning = $"Same attribute detected: {fact.Predicate}"
                    });
                }
            }
        }

        return Task.FromResult(new FactValidationResult
        {
            IsValid = true,
            ConflictType = FactConflictType.None,
            RecommendedAction = FactConflictAction.Add,
            Confidence = 1.0f,
            Reasoning = "No conflicts detected"
        });
    }

    private static StatementContextType DetectContextType(string content)
    {
        var lower = content.ToLowerInvariant();

        // Quoted content markers
        if (QuotedPattern().IsMatch(content) ||
            lower.Contains("in the book") ||
            lower.Contains("in the novel") ||
            lower.Contains("in the movie") ||
            lower.Contains("he said") ||
            lower.Contains("she said") ||
            lower.Contains("소설에서") ||
            lower.Contains("책에서"))
        {
            return StatementContextType.Quoted;
        }

        // Hypothetical markers
        if (lower.Contains("if i were") ||
            lower.Contains("suppose") ||
            lower.Contains("what if") ||
            lower.Contains("만약") ||
            lower.Contains("가정"))
        {
            return StatementContextType.Hypothetical;
        }

        // Role-play markers
        if (lower.Contains("as the") ||
            lower.Contains("playing as") ||
            lower.Contains("역할로") ||
            lower.Contains("인 척"))
        {
            return StatementContextType.RolePlay;
        }

        // Question markers
        if (content.TrimEnd().EndsWith('?') ||
            lower.StartsWith("is my") ||
            lower.StartsWith("what is my") ||
            lower.StartsWith("do i"))
        {
            return StatementContextType.Question;
        }

        // Historical markers
        if (lower.Contains("used to") ||
            lower.Contains("before") ||
            lower.Contains("예전에") ||
            lower.Contains("과거에"))
        {
            return StatementContextType.Historical;
        }

        return StatementContextType.Direct;
    }

    // Regex patterns for fact extraction
    [GeneratedRegex(@"(?:my name is|i'm called|call me|내 이름은|제 이름은)\s+([^\.,!?\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"(?:i am|i'm)\s+(\d+)\s*(?:years old|살)", RegexOptions.IgnoreCase)]
    private static partial Regex AgePattern();

    [GeneratedRegex(@"(?:i (?:like|love|enjoy)|나는?\s+.+(?:를|을)?\s*좋아)", RegexOptions.IgnoreCase)]
    private static partial Regex PreferencePattern();

    [GeneratedRegex(@"(?:i (?:work as|am a|am an)|직업은)\s+([^\.,!?\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OccupationPattern();

    [GeneratedRegex(@"(?:i live in|i'm from|살고 있|살아)\s+([^\.,!?\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LocationPattern();

    [GeneratedRegex(@"[""'""].*?[""'""]", RegexOptions.None)]
    private static partial Regex QuotedPattern();
}
