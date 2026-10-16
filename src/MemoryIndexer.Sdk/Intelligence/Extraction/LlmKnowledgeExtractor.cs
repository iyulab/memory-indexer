using System.Text.Json;

using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Extraction;

/// <summary>
/// LLM-based knowledge extractor using prompt engineering for Q&amp;A exchanges.
/// Phase 25: Semantic Knowledge Extraction.
/// </summary>
/// <remarks>
/// Extracts factual knowledge from conversational Q&amp;A pairs to generate
/// Semantic memories, addressing memory type imbalance.
///
/// Uses language models to:
/// - Extract facts from any language (multilingual support)
/// - Handle complex patterns beyond simple regex
/// - Understand context and nuance in responses
/// - Generate confidence scores based on semantic understanding
/// </remarks>
public sealed partial class LlmKnowledgeExtractor : IKnowledgeExtractor
{
    private readonly ITextCompletionService _completionService;
    private readonly ILogger<LlmKnowledgeExtractor> _logger;

    public LlmKnowledgeExtractor(
        ITextCompletionService completionService,
        ILogger<LlmKnowledgeExtractor> logger)
    {
        _completionService = completionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExtractedFact>> ExtractAsync(
        KnowledgeExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildExtractionPrompt(context);

            var options = new TextCompletionOptions
            {
                Temperature = 0.1f,  // Low temperature for deterministic extraction
                MaxTokens = 500,
                StopSequences = new[] { "###" },
                ResponseFormat = CompletionResponseFormat.Json,
            };

            LogExtractingKnowledgeQuestionAnswer(_logger, context.Question.Length, context.Answer.Length);

            var (result, error) = await LlmJson.CompleteAsync<ExtractionResult>(_completionService, prompt, options, cancellationToken);
            if (result is null)
            {
                LogUnreadableExtraction(_logger, error);
                return Array.Empty<ExtractedFact>();
            }

            var facts = result.Facts ?? [];

            LogExtractedCountFacts(_logger, facts.Count);

            return facts;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailedExtractKnowledge(_logger, ex);
            return Array.Empty<ExtractedFact>();
        }
    }

    private static string BuildExtractionPrompt(KnowledgeExtractionContext context)
    {
        var subject = context.Subject ?? "it";

        return $$"""
            Extract factual knowledge from the following Q&A exchange.

            Question: {{context.Question}}
            Answer: {{context.Answer}}
            Subject: {{subject}}

            Instructions:
            1. Convert the Q&A into declarative factual statements
            2. Assess confidence (0.0-1.0) based on answer certainty
            3. Assess importance (0.0-1.0) based on information value
            4. Return ONLY one JSON object, no explanation and no code fence; confidence and importance are numbers from 0.0 to 1.0

            Output format (an example):
            {
              "facts": [
                {
                  "content": "declarative fact about {{subject}}",
                  "confidence": 0.85,
                  "importance": 0.7,
                  "source": "extraction method"
                }
              ]
            }

            Guidelines:
            - If answer is "yes": high confidence (0.8-0.9)
            - If answer is "no": very high confidence (0.85-0.95)
            - If answer is "maybe/uncertain": low confidence (0.4-0.6)
            - Category assertions (is a X): importance 0.7-0.8
            - Property assertions (is blue): importance 0.6-0.7
            - Capability assertions (can do X): importance 0.5-0.6

            ###
            """;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracting knowledge from Q&A (question length {QuestionLength}, answer length {AnswerLength})")]
    private static partial void LogExtractingKnowledgeQuestionAnswer(ILogger logger, int questionLength, int answerLength);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {Count} facts from Q&A")]
    private static partial void LogExtractedCountFacts(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to extract knowledge from Q&A")]
    private static partial void LogFailedExtractKnowledge(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge extraction reply could not be read as JSON, also after one retry ({Reason}); no facts extracted")]
    private static partial void LogUnreadableExtraction(ILogger logger, string? reason);
}

#region JSON Models

internal sealed class ExtractionResult
{
    [JsonPropertyName("facts")]
    public List<ExtractedFact>? Facts { get; set; }
}

#endregion
