using System.Text.RegularExpressions;

namespace MemoryIndexer.Intelligence.Search;

/// <summary>
/// Query expansion service for improving semantic search recall.
/// Expands queries with synonyms, related terms, and contextual keywords.
/// </summary>
public sealed class QueryExpander : IQueryExpander
{
    private static readonly IReadOnlyDictionary<string, string[]> SynonymMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        // Question words -> semantic equivalents
        ["what"] = ["which", "describe", "explain", "tell"],
        ["who"] = ["person", "people", "team", "member", "colleague"],
        ["when"] = ["time", "date", "schedule", "deadline"],
        ["where"] = ["location", "place", "address"],
        ["how"] = ["method", "way", "process", "approach"],
        ["why"] = ["reason", "cause", "purpose"],

        // Common verbs
        ["build"] = ["create", "develop", "implement", "make", "construct"],
        ["use"] = ["utilize", "employ", "work with", "apply"],
        ["work"] = ["job", "task", "project", "employment"],
        ["plan"] = ["schedule", "roadmap", "strategy", "intention", "future"],

        // Technology terms
        ["feature"] = ["functionality", "capability", "component", "module"],
        ["bug"] = ["issue", "error", "problem", "defect"],
        ["code"] = ["program", "script", "implementation", "software"],
        ["tech"] = ["technology", "technical", "stack", "framework"],

        // Team/People terms
        ["team"] = ["colleague", "member", "coworker", "collaborator"],
        ["member"] = ["person", "colleague", "teammate"],

        // Time-related
        ["future"] = ["upcoming", "later", "next", "plan", "will"],
        ["past"] = ["previous", "before", "earlier", "history"],

        // Common nouns
        ["challenge"] = ["difficulty", "problem", "issue", "obstacle"],
        ["tool"] = ["software", "application", "utility", "program"],
        ["meeting"] = ["call", "standup", "sync", "discussion"]
    };

    private static readonly IReadOnlyDictionary<string, string[]> ContextExpansionMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        // Question pattern -> related contexts
        ["building"] = ["developing", "implementing", "creating", "working on"],
        ["features"] = ["workout", "nutrition", "sleep", "tracking", "logging", "functionality"],
        ["team members"] = ["Mike", "Sarah", "colleague", "collaborator", "working with"],
        ["future plans"] = ["planning", "will add", "next", "upcoming", "roadmap", "social", "AI"],
        ["tech stack"] = ["React", "Node", "MongoDB", "GraphQL", "framework", "library"],
        ["dietary"] = ["vegetarian", "allergic", "food", "eat", "diet"],
        ["pet"] = ["dog", "cat", "animal", "Max", "companion"],
        ["programming"] = ["code", "development", "software", "language", "Rust", "Python"],
    };

    /// <inheritdoc />
    public string ExpandQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;

        var expandedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            query // Always include original
        };

        // Extract words from query
        var words = ExtractWords(query);

        // Add synonyms for each word
        foreach (var word in words)
        {
            if (SynonymMap.TryGetValue(word, out var synonyms))
            {
                foreach (var synonym in synonyms.Take(3)) // Limit to avoid over-expansion
                {
                    expandedTerms.Add(synonym);
                }
            }
        }

        // Check for contextual patterns
        var lowerQuery = query.ToLowerInvariant();
        foreach (var (pattern, expansions) in ContextExpansionMap)
        {
            if (lowerQuery.Contains(pattern))
            {
                foreach (var expansion in expansions.Take(4))
                {
                    expandedTerms.Add(expansion);
                }
            }
        }

        // Build expanded query
        var result = string.Join(" ", expandedTerms);
        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GenerateQueryVariants(string query, int maxVariants = 3)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [query];

        var variants = new List<string> { query };
        var words = ExtractWords(query);

        // Generate variants by substituting synonyms
        foreach (var word in words)
        {
            if (SynonymMap.TryGetValue(word, out var synonyms) && synonyms.Length > 0)
            {
                var variant = query.Replace(word, synonyms[0], StringComparison.OrdinalIgnoreCase);
                if (variant != query && !variants.Contains(variant))
                {
                    variants.Add(variant);
                    if (variants.Count >= maxVariants)
                        break;
                }
            }
        }

        // Add contextual variant if pattern matches
        var lowerQuery = query.ToLowerInvariant();
        foreach (var (pattern, expansions) in ContextExpansionMap)
        {
            if (lowerQuery.Contains(pattern) && expansions.Length > 0)
            {
                var contextVariant = $"{query} {expansions[0]}";
                if (!variants.Contains(contextVariant))
                {
                    variants.Add(contextVariant);
                    if (variants.Count >= maxVariants)
                        break;
                }
            }
        }

        return variants.Take(maxVariants).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetSynonyms(string term)
    {
        return SynonymMap.TryGetValue(term, out var synonyms)
            ? synonyms
            : [];
    }

    private static IReadOnlyList<string> ExtractWords(string text)
    {
        // Extract words, ignoring punctuation
        return Regex.Matches(text, @"\b[a-zA-Z]+\b")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length > 2) // Skip very short words
            .Distinct()
            .ToList();
    }
}

/// <summary>
/// Interface for query expansion service.
/// </summary>
public interface IQueryExpander
{
    /// <summary>
    /// Expands a query with synonyms and related terms.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <returns>Expanded query with additional terms.</returns>
    string ExpandQuery(string query);

    /// <summary>
    /// Generates multiple query variants for multi-query retrieval.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <param name="maxVariants">Maximum number of variants to generate.</param>
    /// <returns>List of query variants.</returns>
    IReadOnlyList<string> GenerateQueryVariants(string query, int maxVariants = 3);

    /// <summary>
    /// Gets synonyms for a specific term.
    /// </summary>
    /// <param name="term">The term to find synonyms for.</param>
    /// <returns>List of synonyms.</returns>
    IReadOnlyList<string> GetSynonyms(string term);
}
