using System.Text;
using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Search;

/// <summary>
/// HyDE (Hypothetical Document Embeddings) query expander.
/// Generates hypothetical documents that would answer the query,
/// then uses those for retrieval instead of the raw query.
/// </summary>
/// <remarks>
/// Based on: "Precise Zero-Shot Dense Retrieval without Relevance Labels" (Gao et al., 2022)
///
/// This implementation uses template-based generation for predictable, fast hypothetical
/// document creation. Can be extended with LLM-based generation for more sophisticated
/// hypothetical documents.
///
/// Key insight: Documents in the corpus are typically statements/facts, while queries
/// are questions. HyDE bridges this gap by generating document-like text from questions.
/// </remarks>
public sealed partial class HydeQueryExpander : IHydeQueryExpander
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<HydeQueryExpander> _logger;

    /// <summary>
    /// Templates for generating hypothetical documents based on query patterns.
    /// </summary>
    private static readonly (Regex Pattern, string[] Templates)[] HypotheticalTemplates =
    [
        // Who questions → Person-focused hypothetical documents
        (WhoPattern(), [
            "The person responsible is {subject}. They {action}.",
            "{subject} is the one who {action}. Their role involves this.",
            "This was done by {subject}, who has expertise in {topic}."
        ]),

        // What questions → Definition/explanation hypothetical documents
        (WhatPattern(), [
            "{topic} is {definition}. It involves {details}.",
            "The {topic} refers to {definition}. Key aspects include {details}.",
            "{topic}: {definition}. This is important because {reason}."
        ]),

        // When questions → Temporal hypothetical documents
        (WhenPattern(), [
            "This happened on {date}. The event occurred during {context}.",
            "The timeline shows this was {date}. It was scheduled for {context}.",
            "{event} took place {date}, which was {context}."
        ]),

        // Where questions → Location-focused hypothetical documents
        (WherePattern(), [
            "This is located at {location}. The place is {description}.",
            "The location is {location}. It can be found {description}.",
            "{subject} is at {location}, specifically {description}."
        ]),

        // Why questions → Reason/explanation hypothetical documents
        (WhyPattern(), [
            "The reason is {reason}. This happened because {explanation}.",
            "This occurs because {reason}. The underlying cause is {explanation}.",
            "{topic} happens due to {reason}. The explanation involves {explanation}."
        ]),

        // How questions → Process/method hypothetical documents
        (HowPattern(), [
            "To do this, you need to {steps}. The process involves {details}.",
            "The method is: {steps}. Key considerations include {details}.",
            "Here's how: {steps}. This approach works because {reason}."
        ]),

        // Preference questions → User preference hypothetical documents
        (PreferencePattern(), [
            "The user prefers {preference}. They typically choose {details}.",
            "Based on past interactions, the preference is {preference}.",
            "The preferred option is {preference}, as indicated by {evidence}."
        ]),

        // Default pattern for general questions
        (DefaultPattern(), [
            "Regarding {topic}: {answer}. This information is relevant because {context}.",
            "The answer to this is {answer}. Key points about {topic} include {details}.",
            "{topic} involves {answer}. Important context: {context}."
        ])
    ];

    public HydeQueryExpander(
        IEmbeddingService embeddingService,
        ILogger<HydeQueryExpander> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateHypotheticalEmbeddingAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var hypotheticalDoc = GenerateHypotheticalDocument(query);

        _logger.LogDebug(
            "HyDE: Query '{Query}' → Hypothetical '{Hypothetical}'",
            query.Length > 50 ? query[..50] + "..." : query,
            hypotheticalDoc.Length > 80 ? hypotheticalDoc[..80] + "..." : hypotheticalDoc);

        return await _embeddingService.GenerateEmbeddingAsync(hypotheticalDoc, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateMultipleHypotheticalEmbeddingsAsync(
        string query,
        int count = 3,
        CancellationToken cancellationToken = default)
    {
        var hypotheticalDocs = GenerateMultipleHypotheticalDocuments(query, count);

        _logger.LogDebug(
            "HyDE: Generated {Count} hypothetical documents for '{Query}'",
            hypotheticalDocs.Count,
            query.Length > 50 ? query[..50] + "..." : query);

        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(
            hypotheticalDocs, cancellationToken);

        return embeddings;
    }

    /// <inheritdoc />
    public string GenerateHypotheticalDocument(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;

        var lowerQuery = query.ToLowerInvariant();
        var extractedTerms = ExtractQueryTerms(query);

        // Find matching template
        foreach (var (pattern, templates) in HypotheticalTemplates)
        {
            if (pattern.IsMatch(lowerQuery))
            {
                var template = templates[0]; // Use first template
                return FillTemplate(template, extractedTerms, query);
            }
        }

        // Fallback: Convert question to statement
        return ConvertQuestionToStatement(query);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GenerateMultipleHypotheticalDocuments(string query, int count = 3)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [query];

        var documents = new List<string>();
        var lowerQuery = query.ToLowerInvariant();
        var extractedTerms = ExtractQueryTerms(query);

        // Find matching template set
        foreach (var (pattern, templates) in HypotheticalTemplates)
        {
            if (pattern.IsMatch(lowerQuery))
            {
                foreach (var template in templates.Take(count))
                {
                    documents.Add(FillTemplate(template, extractedTerms, query));
                }
                break;
            }
        }

        // Ensure we have at least one document
        if (documents.Count == 0)
        {
            documents.Add(ConvertQuestionToStatement(query));
        }

        // Add query statement conversion as additional document
        if (documents.Count < count)
        {
            documents.Add(ConvertQuestionToStatement(query));
        }

        return documents.Take(count).ToList();
    }

    private static string FillTemplate(string template, QueryTerms terms, string originalQuery)
    {
        var result = template;

        // Fill in extracted terms
        result = result.Replace("{subject}", terms.Subject ?? "the subject");
        result = result.Replace("{topic}", terms.Topic ?? ExtractMainTopic(originalQuery));
        result = result.Replace("{action}", terms.Action ?? "performs this action");
        result = result.Replace("{definition}", terms.Definition ?? "a concept related to this");
        result = result.Replace("{details}", terms.Details ?? "relevant details");
        result = result.Replace("{reason}", terms.Reason ?? "specific reasons");
        result = result.Replace("{explanation}", terms.Explanation ?? "the underlying factors");
        result = result.Replace("{date}", terms.Date ?? "a specific time");
        result = result.Replace("{context}", terms.Context ?? "the given context");
        result = result.Replace("{location}", terms.Location ?? "a specific location");
        result = result.Replace("{description}", terms.Description ?? "the relevant area");
        result = result.Replace("{steps}", terms.Steps ?? "follow these steps");
        result = result.Replace("{preference}", terms.Preference ?? "this option");
        result = result.Replace("{evidence}", terms.Evidence ?? "previous interactions");
        result = result.Replace("{event}", terms.Event ?? "the event");
        result = result.Replace("{answer}", terms.Answer ?? ConvertQuestionToStatement(originalQuery));

        return result;
    }

    private static QueryTerms ExtractQueryTerms(string query)
    {
        var terms = new QueryTerms();
        var lowerQuery = query.ToLowerInvariant();

        // Extract topic (main noun phrase after question word)
        var topicMatch = TopicExtractionPattern().Match(query);
        if (topicMatch.Success)
        {
            terms.Topic = topicMatch.Groups[1].Value.Trim();
        }

        // Extract subject (capitalized words that might be names)
        var nameMatches = NamePattern().Matches(query);
        if (nameMatches.Count > 0)
        {
            terms.Subject = string.Join(" ", nameMatches.Select(m => m.Value));
        }

        // Extract preference indicators
        if (lowerQuery.Contains("prefer") || lowerQuery.Contains("like") || lowerQuery.Contains("favorite"))
        {
            var prefMatch = PreferenceExtractionPattern().Match(lowerQuery);
            if (prefMatch.Success)
            {
                terms.Preference = prefMatch.Groups[1].Value.Trim();
            }
        }

        return terms;
    }

    private static string ExtractMainTopic(string query)
    {
        // Remove question words and extract main content
        var cleaned = QuestionWordPattern().Replace(query, "").Trim();
        cleaned = cleaned.TrimEnd('?', '.', '!');

        // Take first few meaningful words
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Take(5);

        return string.Join(" ", words);
    }

    private static string ConvertQuestionToStatement(string query)
    {
        var statement = new StringBuilder();
        var lowerQuery = query.ToLowerInvariant().TrimEnd('?', '.', '!');

        // Remove question word and restructure
        var cleaned = QuestionWordPattern().Replace(lowerQuery, "").Trim();

        // Handle "is/are" questions
        if (lowerQuery.StartsWith("is ") || lowerQuery.StartsWith("are "))
        {
            cleaned = lowerQuery[lowerQuery.IndexOf(' ')..].Trim();
            statement.Append(char.ToUpper(cleaned[0]) + cleaned[1..]);
            statement.Append(" is confirmed.");
        }
        // Handle "do/does" questions
        else if (lowerQuery.StartsWith("do ") || lowerQuery.StartsWith("does "))
        {
            cleaned = lowerQuery[(lowerQuery.IndexOf(' ') + 1)..].Trim();
            statement.Append(char.ToUpper(cleaned[0]) + cleaned[1..]);
            statement.Append('.');
        }
        // Handle "can/could/will/would" questions
        else if (lowerQuery.StartsWith("can ") || lowerQuery.StartsWith("could ") ||
                 lowerQuery.StartsWith("will ") || lowerQuery.StartsWith("would "))
        {
            cleaned = lowerQuery[(lowerQuery.IndexOf(' ') + 1)..].Trim();
            statement.Append("It is possible that ");
            statement.Append(cleaned);
            statement.Append('.');
        }
        // General question → statement
        else
        {
            var topic = ExtractMainTopic(query);
            statement.Append("Information about ");
            statement.Append(topic);
            statement.Append(": this relates to the query context.");
        }

        return statement.ToString();
    }

    // Regex patterns using source generators
    [GeneratedRegex(@"^who\s", RegexOptions.IgnoreCase)]
    private static partial Regex WhoPattern();

    [GeneratedRegex(@"^what\s", RegexOptions.IgnoreCase)]
    private static partial Regex WhatPattern();

    [GeneratedRegex(@"^when\s", RegexOptions.IgnoreCase)]
    private static partial Regex WhenPattern();

    [GeneratedRegex(@"^where\s", RegexOptions.IgnoreCase)]
    private static partial Regex WherePattern();

    [GeneratedRegex(@"^why\s", RegexOptions.IgnoreCase)]
    private static partial Regex WhyPattern();

    [GeneratedRegex(@"^how\s", RegexOptions.IgnoreCase)]
    private static partial Regex HowPattern();

    [GeneratedRegex(@"(prefer|like|favorite|favourite)", RegexOptions.IgnoreCase)]
    private static partial Regex PreferencePattern();

    [GeneratedRegex(@".*")]
    private static partial Regex DefaultPattern();

    [GeneratedRegex(@"(?:what|who|where|when|why|how|which)\s+(?:is|are|was|were|do|does|did|can|could|will|would)?\s*(.+?)(?:\?|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TopicExtractionPattern();

    [GeneratedRegex(@"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"(?:prefer|like|favorite|favourite)\s+(.+?)(?:\.|$)")]
    private static partial Regex PreferenceExtractionPattern();

    [GeneratedRegex(@"^(?:what|who|where|when|why|how|which|is|are|do|does|did|can|could|will|would)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionWordPattern();

    /// <summary>
    /// Extracted terms from query analysis.
    /// </summary>
    private sealed class QueryTerms
    {
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? Action { get; set; }
        public string? Definition { get; set; }
        public string? Details { get; set; }
        public string? Reason { get; set; }
        public string? Explanation { get; set; }
        public string? Date { get; set; }
        public string? Context { get; set; }
        public string? Location { get; set; }
        public string? Description { get; set; }
        public string? Steps { get; set; }
        public string? Preference { get; set; }
        public string? Evidence { get; set; }
        public string? Event { get; set; }
        public string? Answer { get; set; }
    }
}

/// <summary>
/// Interface for HyDE query expansion.
/// </summary>
public interface IHydeQueryExpander
{
    /// <summary>
    /// Generates an embedding from a hypothetical document based on the query.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embedding of the hypothetical document.</returns>
    Task<ReadOnlyMemory<float>> GenerateHypotheticalEmbeddingAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates multiple hypothetical document embeddings for ensemble retrieval.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <param name="count">Number of hypothetical documents to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embeddings of the hypothetical documents.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateMultipleHypotheticalEmbeddingsAsync(
        string query,
        int count = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a single hypothetical document from a query.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <returns>Hypothetical document text.</returns>
    string GenerateHypotheticalDocument(string query);

    /// <summary>
    /// Generates multiple hypothetical documents for diversity.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <param name="count">Number of documents to generate.</param>
    /// <returns>List of hypothetical documents.</returns>
    IReadOnlyList<string> GenerateMultipleHypotheticalDocuments(string query, int count = 3);
}
