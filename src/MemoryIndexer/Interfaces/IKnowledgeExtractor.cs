namespace MemoryIndexer.Interfaces;

/// <summary>
/// Extracts factual knowledge from conversational exchanges.
/// Phase 25: Semantic Knowledge Extraction.
/// </summary>
/// <remarks>
/// Addresses memory type imbalance by generating Semantic memories
/// from Q&A interactions that would otherwise only create Episodic memories.
///
/// Example:
/// Q: "Is it a liquid?" A: "Maybe" (Subject: "the ocean")
/// → Extract: "The ocean is primarily liquid"
/// → Extract: "The ocean has both liquid and solid components"
/// </remarks>
public interface IKnowledgeExtractor
{
    /// <summary>
    /// Extracts factual knowledge from a conversational exchange.
    /// </summary>
    /// <param name="context">Context containing question, answer, and subject.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of extracted facts (Semantic knowledge).</returns>
    Task<IReadOnlyList<ExtractedFact>> ExtractAsync(
        KnowledgeExtractionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for knowledge extraction from Q&A exchanges.
/// </summary>
public sealed class KnowledgeExtractionContext
{
    /// <summary>
    /// The question asked (e.g., "Is it a liquid?").
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// The answer given (e.g., "Yes", "No", "Maybe").
    /// </summary>
    public required string Answer { get; init; }

    /// <summary>
    /// The subject being discussed (e.g., "the ocean").
    /// Optional - if not provided, extractor may use generic subject.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// User ID for context-specific extraction.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Additional context metadata (e.g., conversation domain, round number).
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// A single extracted fact from a conversational exchange.
/// </summary>
public sealed class ExtractedFact
{
    /// <summary>
    /// The factual statement extracted (e.g., "The ocean is a liquid").
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Confidence in the extraction (0-1).
    /// Higher confidence = more certain this fact is valid.
    /// </summary>
    public float Confidence { get; init; } = 0.7f;

    /// <summary>
    /// Importance score for the extracted fact (0-1).
    /// Higher importance = more valuable for future reasoning.
    /// </summary>
    public float Importance { get; init; } = 0.6f;

    /// <summary>
    /// Source of the extraction (e.g., "Pattern: IsIt_Yes").
    /// Used for debugging and validation.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Topics related to this fact.
    /// </summary>
    public List<string>? Topics { get; init; }

    /// <summary>
    /// Entities mentioned in this fact.
    /// </summary>
    public List<string>? Entities { get; init; }
}
