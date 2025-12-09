using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MemoryIndexer.Intelligence.Search;

/// <summary>
/// BM25 sparse retrieval index for keyword-based search.
/// Implements the Okapi BM25 ranking function.
/// </summary>
public sealed partial class BM25Index
{
    private readonly ConcurrentDictionary<string, TermInfo> _termIndex = new();
    private readonly ConcurrentDictionary<Guid, DocumentInfo> _documents = new();
    private readonly object _statsLock = new();

    private double _averageDocLength;
    private int _documentCount;

    /// <summary>
    /// BM25 k1 parameter: term frequency saturation.
    /// Higher values = term frequency matters more.
    /// Typical range: 1.2-2.0
    /// </summary>
    public double K1 { get; init; } = 1.5;

    /// <summary>
    /// BM25 b parameter: document length normalization.
    /// 0 = no normalization, 1 = full normalization.
    /// Typical range: 0.5-0.8
    /// </summary>
    public double B { get; init; } = 0.75;

    /// <summary>
    /// Adds a document to the index.
    /// </summary>
    public void AddDocument(Guid id, string content)
    {
        var tokens = Tokenize(content);
        var termFrequencies = tokens
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        var docInfo = new DocumentInfo
        {
            Id = id,
            Length = tokens.Count,
            TermFrequencies = termFrequencies
        };

        _documents[id] = docInfo;

        // Update term index
        foreach (var (term, _) in termFrequencies)
        {
            _termIndex.AddOrUpdate(
                term,
                _ => new TermInfo { DocumentFrequency = 1, DocumentIds = [id] },
                (_, existing) =>
                {
                    existing.DocumentFrequency++;
                    existing.DocumentIds.Add(id);
                    return existing;
                });
        }

        UpdateStats();
    }

    /// <summary>
    /// Removes a document from the index.
    /// </summary>
    public void RemoveDocument(Guid id)
    {
        if (!_documents.TryRemove(id, out var docInfo))
            return;

        // Update term index
        foreach (var term in docInfo.TermFrequencies.Keys)
        {
            if (_termIndex.TryGetValue(term, out var termInfo))
            {
                termInfo.DocumentIds.Remove(id);
                termInfo.DocumentFrequency--;

                if (termInfo.DocumentFrequency <= 0)
                {
                    _termIndex.TryRemove(term, out _);
                }
            }
        }

        UpdateStats();
    }

    /// <summary>
    /// Searches the index and returns BM25 scores.
    /// </summary>
    public IReadOnlyList<(Guid Id, float Score)> Search(string query, int limit = 10)
    {
        var queryTokens = Tokenize(query);
        var scores = new Dictionary<Guid, double>();

        foreach (var term in queryTokens.Distinct())
        {
            if (!_termIndex.TryGetValue(term, out var termInfo))
                continue;

            var idf = CalculateIdf(termInfo.DocumentFrequency);

            foreach (var docId in termInfo.DocumentIds)
            {
                if (!_documents.TryGetValue(docId, out var docInfo))
                    continue;

                if (!docInfo.TermFrequencies.TryGetValue(term, out var tf))
                    continue;

                var score = CalculateBM25Score(tf, docInfo.Length, idf);

                if (!scores.TryAdd(docId, score))
                {
                    scores[docId] += score;
                }
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => (kv.Key, (float)kv.Value))
            .ToList();
    }

    /// <summary>
    /// Calculates BM25 score for a single term.
    /// </summary>
    private double CalculateBM25Score(int termFrequency, int docLength, double idf)
    {
        var numerator = termFrequency * (K1 + 1);
        var denominator = termFrequency + K1 * (1 - B + B * docLength / _averageDocLength);
        return idf * numerator / denominator;
    }

    /// <summary>
    /// Calculates Inverse Document Frequency.
    /// </summary>
    private double CalculateIdf(int documentFrequency)
    {
        // Using Robertson's IDF formula with smoothing
        var n = _documentCount;
        var df = documentFrequency;
        return Math.Log((n - df + 0.5) / (df + 0.5) + 1);
    }

    private void UpdateStats()
    {
        lock (_statsLock)
        {
            _documentCount = _documents.Count;
            _averageDocLength = _documentCount > 0
                ? _documents.Values.Average(d => d.Length)
                : 0;
        }
    }

    /// <summary>
    /// Tokenizes text into terms.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Convert to lowercase and split on non-word characters
        var normalized = text.ToLowerInvariant();
        var tokens = TokenizerRegex().Split(normalized)
            .Where(t => t.Length >= 2) // Filter very short tokens
            .ToList();

        return tokens;
    }

    [GeneratedRegex(@"\W+", RegexOptions.Compiled)]
    private static partial Regex TokenizerRegex();

    private sealed class TermInfo
    {
        public int DocumentFrequency { get; set; }
        public HashSet<Guid> DocumentIds { get; init; } = [];
    }

    private sealed class DocumentInfo
    {
        public Guid Id { get; init; }
        public int Length { get; init; }
        public Dictionary<string, int> TermFrequencies { get; init; } = [];
    }
}
