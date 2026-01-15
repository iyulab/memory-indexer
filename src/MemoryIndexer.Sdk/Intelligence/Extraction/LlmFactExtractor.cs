using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Extraction;

/// <summary>
/// LLM-based fact extractor for direct statements with context awareness.
/// Distinguishes user facts from quoted/fictional content.
/// </summary>
/// <remarks>
/// Key capabilities:
/// - Context detection: Direct vs Quoted vs Hypothetical vs Narrative
/// - Confidence scoring based on linguistic markers
/// - Promotion path recommendation (FastTrack, Standard, SessionOnly)
/// - Conflict detection with existing facts
///
/// Example:
/// - "My name is John" → FastTrack (high confidence direct statement)
/// - "In the book, the hero says 'My name is Lincoln'" → SessionOnly (quoted)
/// </remarks>
public sealed class LlmFactExtractor : IFactExtractor
{
    private readonly ITextCompletionService _completionService;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<LlmFactExtractor> _logger;

    /// <summary>
    /// Confidence threshold for FastTrack promotion (direct to User scope).
    /// </summary>
    public const float FastTrackThreshold = 0.9f;

    /// <summary>
    /// Confidence threshold for Standard promotion path.
    /// </summary>
    public const float StandardThreshold = 0.5f;

    /// <summary>
    /// Minimum confidence for conflict detection warnings.
    /// </summary>
    public const float ConflictWarningThreshold = 0.7f;

    public LlmFactExtractor(
        ITextCompletionService completionService,
        ILogger<LlmFactExtractor> logger,
        IEmbeddingService? embeddingService = null)
    {
        _completionService = completionService;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FactExtractionResult> ExtractAsync(
        FactExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildExtractionPrompt(context);

            var options = new TextCompletionOptions
            {
                Temperature = 0.1f,  // Low temperature for deterministic extraction
                MaxTokens = 800,
                StopSequences = ["###"]
            };

            _logger.LogDebug("Extracting facts from content: {Content}",
                context.Content.Length > 100 ? context.Content[..100] + "..." : context.Content);

            var response = await _completionService.CompleteAsync(prompt, options, cancellationToken);

            var result = ParseExtractionResponse(response);

            _logger.LogDebug(
                "Extracted {Count} facts (FastTrack: {FastTrack}, Standard: {Standard}, SessionOnly: {Session})",
                result.Facts.Count,
                result.Facts.Count(f => f.PromotionPath == PromotionPath.FastTrack),
                result.Facts.Count(f => f.PromotionPath == PromotionPath.Standard),
                result.Facts.Count(f => f.PromotionPath == PromotionPath.SessionOnly));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract facts from content");
            return new FactExtractionResult
            {
                Facts = [],
                OverallConfidence = 0f,
                ContextType = StatementContextType.Unknown,
                Reasoning = $"Extraction failed: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<FactValidationResult> ValidateAsync(
        UserFact fact,
        IReadOnlyList<MemoryUnit> existingFacts,
        CancellationToken cancellationToken = default)
    {
        if (existingFacts.Count == 0)
        {
            return new FactValidationResult
            {
                IsValid = true,
                ConflictType = FactConflictType.None,
                RecommendedAction = FactConflictAction.Add,
                Confidence = 1.0f,
                Reasoning = "No existing facts to compare"
            };
        }

        try
        {
            var prompt = BuildValidationPrompt(fact, existingFacts);

            var options = new TextCompletionOptions
            {
                Temperature = 0.1f,
                MaxTokens = 500,
                StopSequences = ["###"]
            };

            _logger.LogDebug("Validating fact against {Count} existing facts: {Content}",
                existingFacts.Count, fact.Content);

            var response = await _completionService.CompleteAsync(prompt, options, cancellationToken);

            var result = ParseValidationResponse(response, existingFacts);

            _logger.LogDebug("Validation result: {ConflictType} -> {Action}",
                result.ConflictType, result.RecommendedAction);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate fact");
            return new FactValidationResult
            {
                IsValid = true,  // Default to allowing the fact on error
                ConflictType = FactConflictType.None,
                RecommendedAction = FactConflictAction.Add,
                Confidence = 0.5f,
                Reasoning = $"Validation failed: {ex.Message}"
            };
        }
    }

    private static string BuildExtractionPrompt(FactExtractionContext context)
    {
        var previousContext = context.PreviousTurns?.Count > 0
            ? $"Previous conversation:\n{string.Join("\n", context.PreviousTurns.TakeLast(3))}\n\n"
            : "";

        var roleInfo = !string.IsNullOrEmpty(context.Role)
            ? $"Speaker role: {context.Role}\n"
            : "";

        return $$"""
            Analyze the following content and extract factual statements about the user.

            {{roleInfo}}{{previousContext}}Content: {{context.Content}}

            Instructions:
            1. Identify the context type (direct statement, quoted text, hypothetical, narrative, roleplay, question)
            2. Extract ONLY facts about the actual user (not quoted/fictional characters)
            3. For each fact, determine:
               - confidence (0.0-1.0): How certain is this fact?
               - importance (0.0-1.0): How valuable is this for future context?
               - category: identity, preference, relationship, location, temporal, skill, goal, health, professional, general
               - promotionPath: FastTrack (≥0.9 confidence), Standard (0.5-0.9), SessionOnly (<0.5 or context-dependent), Discard (fictional/irrelevant)
            4. Return ONLY valid JSON, no explanations

            Context Detection Rules:
            - Direct: First-person statements ("My name is...", "I like...", "I am...")
            - Quoted: Text within quotes, citations, book/movie references ("In the novel...", "He said...")
            - Hypothetical: Conditional statements ("If I were...", "Suppose my name was...")
            - Narrative: Story-telling context ("The character...", "In my story...")
            - RolePlay: Acting as someone else ("As the wizard...", "Playing as...")
            - Question: Interrogative statements ("Is my name..?", "What if...?")
            - Historical: Past facts that may no longer be true ("I used to...", "Before...")

            Confidence Scoring:
            - Direct first-person identity facts: 0.90-0.95
            - Direct first-person preferences: 0.80-0.90
            - Confirmed/repeated facts: +0.05 per confirmation
            - Quoted/fictional context: 0.10-0.30 (SessionOnly)
            - Hypothetical/uncertain: 0.30-0.50 (SessionOnly)
            - User role (not assistant): +0.10 bonus

            Output format:
            {
              "contextType": "Direct|Quoted|Hypothetical|Narrative|RolePlay|Question|Historical|Unknown",
              "overallConfidence": 0.0-1.0,
              "reasoning": "explanation of analysis",
              "facts": [
                {
                  "content": "User's name is John",
                  "category": "Identity",
                  "confidence": 0.95,
                  "importance": 0.9,
                  "promotionPath": "FastTrack",
                  "subject": "user",
                  "predicate": "name",
                  "object": "John",
                  "entities": ["John"],
                  "source": "direct statement"
                }
              ]
            }

            IMPORTANT:
            - Do NOT extract facts from quoted text about other people/characters
            - If content is clearly about a fictional character, set promotionPath to "Discard"
            - Identity facts (name, age, occupation) from direct statements get FastTrack
            - Preferences require higher threshold for FastTrack (0.95+)

            ###
            """;
    }

    private static string BuildValidationPrompt(UserFact newFact, IReadOnlyList<MemoryUnit> existingFacts)
    {
        var existingFactsList = string.Join("\n",
            existingFacts.Select((f, i) => $"  [{i}] {f.Content} (confidence: {f.Confidence:F2})"));

        return $$"""
            Validate a new fact against existing user facts.

            New fact: {{newFact.Content}}
            Category: {{newFact.Category}}
            Confidence: {{newFact.Confidence}}

            Existing facts:
            {{existingFactsList}}

            Instructions:
            1. Check for conflicts with existing facts
            2. Determine conflict type and recommended action
            3. For contradictions, require stricter evidence (higher confidence wins)
            4. Return ONLY valid JSON

            Conflict Types:
            - None: No overlap with existing facts
            - Duplicate: Same information (skip storing)
            - SemanticDuplicate: Same meaning, different words (skip)
            - Contradiction: Opposite/conflicting values (needs resolution)
            - ValueUpdate: Same attribute, new value (may replace)
            - TemporalConflict: Outdated information (archive old, add new)
            - Refinement: New fact adds detail (merge)

            Conflict Resolution Rules:
            - Contradiction with existing: Higher confidence wins (threshold: +0.2 difference)
            - Value update: If new fact has confidence ≥ 0.8, replace old
            - Temporal: Check for temporal markers ("used to", "now", "since")
            - Equal confidence contradiction: RequireReview

            Output format:
            {
              "isValid": true|false,
              "conflictType": "None|Duplicate|SemanticDuplicate|Contradiction|ValueUpdate|TemporalConflict|Refinement",
              "conflictingFactIndex": null|-1|index,
              "recommendedAction": "Add|Skip|Replace|Merge|ArchiveAndAdd|RequireReview",
              "confidence": 0.0-1.0,
              "reasoning": "explanation",
              "mergedContent": "optional merged content if action is Merge"
            }

            ###
            """;
    }

    private FactExtractionResult ParseExtractionResponse(string response)
    {
        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart == -1 || jsonEnd == -1)
            {
                _logger.LogWarning("No JSON found in extraction response");
                return new FactExtractionResult
                {
                    Facts = [],
                    OverallConfidence = 0f,
                    ContextType = StatementContextType.Unknown
                };
            }

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var parsed = JsonSerializer.Deserialize<ExtractionResultJson>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed == null)
            {
                return new FactExtractionResult
                {
                    Facts = [],
                    OverallConfidence = 0f,
                    ContextType = StatementContextType.Unknown
                };
            }

            var contextType = ParseContextType(parsed.ContextType);
            var facts = parsed.Facts?.Select(f => new UserFact
            {
                Content = f.Content ?? "",
                Category = ParseFactCategory(f.Category),
                Confidence = f.Confidence,
                Importance = f.Importance,
                PromotionPath = DeterminePromotionPath(f.Confidence, f.PromotionPath, contextType),
                Subject = f.Subject,
                Predicate = f.Predicate,
                Object = f.Object,
                Entities = f.Entities,
                Source = f.Source
            }).ToList() ?? [];

            return new FactExtractionResult
            {
                Facts = facts,
                OverallConfidence = parsed.OverallConfidence,
                ContextType = contextType,
                Reasoning = parsed.Reasoning
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse extraction response as JSON");
            return new FactExtractionResult
            {
                Facts = [],
                OverallConfidence = 0f,
                ContextType = StatementContextType.Unknown,
                Reasoning = $"Parse error: {ex.Message}"
            };
        }
    }

    private FactValidationResult ParseValidationResponse(
        string response,
        IReadOnlyList<MemoryUnit> existingFacts)
    {
        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart == -1 || jsonEnd == -1)
            {
                return new FactValidationResult
                {
                    IsValid = true,
                    ConflictType = FactConflictType.None,
                    RecommendedAction = FactConflictAction.Add,
                    Confidence = 0.5f
                };
            }

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var parsed = JsonSerializer.Deserialize<ValidationResultJson>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed == null)
            {
                return new FactValidationResult
                {
                    IsValid = true,
                    ConflictType = FactConflictType.None,
                    RecommendedAction = FactConflictAction.Add,
                    Confidence = 0.5f
                };
            }

            MemoryUnit? conflictingMemory = null;
            if (parsed.ConflictingFactIndex.HasValue &&
                parsed.ConflictingFactIndex >= 0 &&
                parsed.ConflictingFactIndex < existingFacts.Count)
            {
                conflictingMemory = existingFacts[parsed.ConflictingFactIndex.Value];
            }

            return new FactValidationResult
            {
                IsValid = parsed.IsValid,
                ConflictType = ParseConflictType(parsed.ConflictType),
                ConflictingMemory = conflictingMemory,
                RecommendedAction = ParseConflictAction(parsed.RecommendedAction),
                Confidence = parsed.Confidence,
                Reasoning = parsed.Reasoning,
                MergedContent = parsed.MergedContent
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse validation response as JSON");
            return new FactValidationResult
            {
                IsValid = true,
                ConflictType = FactConflictType.None,
                RecommendedAction = FactConflictAction.Add,
                Confidence = 0.5f,
                Reasoning = $"Parse error: {ex.Message}"
            };
        }
    }

    private static PromotionPath DeterminePromotionPath(
        float confidence,
        string? suggestedPath,
        StatementContextType contextType)
    {
        // Context-based overrides
        if (contextType is StatementContextType.Quoted or
            StatementContextType.Narrative or
            StatementContextType.RolePlay)
        {
            return PromotionPath.SessionOnly;
        }

        if (contextType == StatementContextType.Question)
        {
            return PromotionPath.Discard;
        }

        // Parse suggested path
        if (!string.IsNullOrEmpty(suggestedPath))
        {
            if (Enum.TryParse<PromotionPath>(suggestedPath, true, out var parsed))
            {
                // Validate: Don't allow FastTrack below threshold
                if (parsed == PromotionPath.FastTrack && confidence < FastTrackThreshold)
                {
                    return PromotionPath.Standard;
                }
                return parsed;
            }
        }

        // Confidence-based determination
        return confidence switch
        {
            >= FastTrackThreshold => PromotionPath.FastTrack,
            >= StandardThreshold => PromotionPath.Standard,
            > 0.2f => PromotionPath.SessionOnly,
            _ => PromotionPath.Discard
        };
    }

    private static StatementContextType ParseContextType(string? contextType)
    {
        if (string.IsNullOrEmpty(contextType))
            return StatementContextType.Unknown;

        return contextType.ToLowerInvariant() switch
        {
            "direct" => StatementContextType.Direct,
            "quoted" => StatementContextType.Quoted,
            "hypothetical" => StatementContextType.Hypothetical,
            "narrative" => StatementContextType.Narrative,
            "roleplay" => StatementContextType.RolePlay,
            "question" => StatementContextType.Question,
            "historical" => StatementContextType.Historical,
            _ => StatementContextType.Unknown
        };
    }

    private static FactCategory ParseFactCategory(string? category)
    {
        if (string.IsNullOrEmpty(category))
            return FactCategory.General;

        return category.ToLowerInvariant() switch
        {
            "identity" => FactCategory.Identity,
            "preference" => FactCategory.Preference,
            "relationship" => FactCategory.Relationship,
            "location" => FactCategory.Location,
            "temporal" => FactCategory.Temporal,
            "skill" => FactCategory.Skill,
            "goal" => FactCategory.Goal,
            "health" => FactCategory.Health,
            "professional" => FactCategory.Professional,
            _ => FactCategory.General
        };
    }

    private static FactConflictType ParseConflictType(string? conflictType)
    {
        if (string.IsNullOrEmpty(conflictType))
            return FactConflictType.None;

        return conflictType.ToLowerInvariant() switch
        {
            "none" => FactConflictType.None,
            "duplicate" => FactConflictType.Duplicate,
            "semanticduplicate" => FactConflictType.SemanticDuplicate,
            "contradiction" => FactConflictType.Contradiction,
            "valueupdate" => FactConflictType.ValueUpdate,
            "temporalconflict" => FactConflictType.TemporalConflict,
            "refinement" => FactConflictType.Refinement,
            _ => FactConflictType.None
        };
    }

    private static FactConflictAction ParseConflictAction(string? action)
    {
        if (string.IsNullOrEmpty(action))
            return FactConflictAction.Add;

        return action.ToLowerInvariant() switch
        {
            "add" => FactConflictAction.Add,
            "skip" => FactConflictAction.Skip,
            "replace" => FactConflictAction.Replace,
            "merge" => FactConflictAction.Merge,
            "archiveandadd" => FactConflictAction.ArchiveAndAdd,
            "requirereview" => FactConflictAction.RequireReview,
            _ => FactConflictAction.Add
        };
    }
}

#region JSON Models

internal sealed class ExtractionResultJson
{
    [JsonPropertyName("contextType")]
    public string? ContextType { get; set; }

    [JsonPropertyName("overallConfidence")]
    public float OverallConfidence { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("facts")]
    public List<FactJson>? Facts { get; set; }
}

internal sealed class FactJson
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("importance")]
    public float Importance { get; set; }

    [JsonPropertyName("promotionPath")]
    public string? PromotionPath { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("predicate")]
    public string? Predicate { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entities")]
    public List<string>? Entities { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

internal sealed class ValidationResultJson
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("conflictType")]
    public string? ConflictType { get; set; }

    [JsonPropertyName("conflictingFactIndex")]
    public int? ConflictingFactIndex { get; set; }

    [JsonPropertyName("recommendedAction")]
    public string? RecommendedAction { get; set; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("mergedContent")]
    public string? MergedContent { get; set; }
}

#endregion
