using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

using EntityRelationType = MemoryIndexer.Interfaces.EntityRelation;
using System.Globalization;

namespace MemoryIndexer.Sdk.Intelligence.Graph;

/// <summary>
/// Expands queries using knowledge graph structure for improved retrieval.
/// Identifies entities in queries and enriches with related graph context.
/// </summary>
/// <remarks>
/// Research basis: Mem0g graph-augmented retrieval, LightRAG entity expansion.
/// Key insight: Queries about "X" benefit from knowing related entities and facts.
/// </remarks>
public sealed partial class GraphQueryExpander : IGraphQueryExpander
{
    private readonly IGraphRetriever _graphRetriever;
    private readonly IImportancePropagator _importancePropagator;
    private readonly ICommunityDetector _communityDetector;
    private readonly ITemporalEntityStore _entityStore;
    private readonly ILogger<GraphQueryExpander> _logger;

    // Common stopwords to filter from entity extraction
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "must", "can", "and", "or", "but", "if",
        "then", "else", "when", "where", "why", "how", "what", "which", "who",
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us",
        "them", "my", "your", "his", "its", "our", "their", "this", "that",
        "about", "with", "from", "into", "during", "before", "after", "above",
        "below", "to", "for", "of", "at", "by", "on", "in", "out", "off",
        "over", "under", "again", "further", "once", "here", "there", "all",
        "any", "each", "few", "more", "most", "some", "such", "no", "not",
        "only", "own", "same", "so", "than", "too", "very", "just", "also"
    };

    public GraphQueryExpander(
        IGraphRetriever graphRetriever,
        IImportancePropagator importancePropagator,
        ICommunityDetector communityDetector,
        ITemporalEntityStore entityStore,
        ILogger<GraphQueryExpander> logger)
    {
        _graphRetriever = graphRetriever;
        _importancePropagator = importancePropagator;
        _communityDetector = communityDetector;
        _entityStore = entityStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExpandedQuery> ExpandQueryAsync(
        string query,
        string userId,
        QueryExpansionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new QueryExpansionOptions();
        var stopwatch = Stopwatch.StartNew();

        LogExpandingQueryUserUserIdQuery(_logger, userId, query);

        // Step 1: Extract entities from query
        var mentionedEntities = await ExtractQueryEntitiesAsync(query, userId, cancellationToken);

        // Step 2: Expand through graph
        var relatedEntities = new List<QueryEntity>();
        var relevantFacts = new List<EntityTriple>();
        var maxHopsExplored = 0;

        foreach (var entity in mentionedEntities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var traversal = await _graphRetriever.TraverseAsync(
                entity.Name,
                new GraphTraversalOptions
                {
                    UserId = userId,
                    MaxHops = options.MaxHops,
                    MaxEntities = options.MaxRelatedEntities
                },
                cancellationToken);

            maxHopsExplored = Math.Max(maxHopsExplored, traversal.Statistics.MaxDepthReached);

            foreach (var discovered in traversal.DiscoveredEntities)
            {
                if (discovered.HopDistance == 0)
                    continue; // Skip the starting entity

                var importance = await _importancePropagator.GetEntityImportanceAsync(
                    discovered.Name, userId, cancellationToken) ?? 0.5f;

                if (importance < options.MinImportanceScore)
                    continue;

                var boost = options.ApplyImportanceBoost ? importance : 1.0f;

                relatedEntities.Add(new QueryEntity
                {
                    Name = discovered.Name,
                    ImportanceScore = importance,
                    Relation = EntityRelationType.GraphRelated,
                    GraphDistance = discovered.HopDistance,
                    Type = discovered.Type,
                    RetrievalBoost = boost
                });

                relevantFacts.AddRange(discovered.Facts);
            }
        }

        // Deduplicate related entities
        relatedEntities = relatedEntities
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.ImportanceScore).First())
            .OrderByDescending(e => e.ImportanceScore)
            .Take(options.MaxRelatedEntities)
            .ToList();

        // Deduplicate facts
        relevantFacts = relevantFacts
            .GroupBy(f => f.Id)
            .Select(g => g.First())
            .OrderByDescending(f => f.Confidence)
            .ToList();

        // Step 3: Get community context
        string? communityContext = null;
        if (options.IncludeCommunityContext && mentionedEntities.Count > 0)
        {
            var entityNames = mentionedEntities.Select(e => e.Name).ToList();
            communityContext = await GetCommunityContextAsync(entityNames, userId, cancellationToken);
        }

        // Step 4: Generate sub-queries
        var subQueries = await GenerateSubQueriesAsync(
            query,
            mentionedEntities,
            new SubQueryOptions { MaxSubQueries = 5 },
            cancellationToken);

        // Step 5: Build expanded query text
        var expandedText = BuildExpandedQueryText(
            query, mentionedEntities, relatedEntities, relevantFacts, communityContext, options);

        stopwatch.Stop();

        LogQueryExpandedMentionedCountMentionedRelatedCount(_logger, mentionedEntities.Count, relatedEntities.Count, relevantFacts.Count, stopwatch.ElapsedMilliseconds);

        return new ExpandedQuery
        {
            OriginalQuery = query,
            ExpandedText = expandedText,
            MentionedEntities = mentionedEntities,
            RelatedEntities = relatedEntities,
            RelevantFacts = relevantFacts,
            CommunityContext = communityContext,
            SubQueries = subQueries,
            Statistics = new ExpansionStatistics
            {
                MentionedEntityCount = mentionedEntities.Count,
                RelatedEntityCount = relatedEntities.Count,
                FactCount = relevantFacts.Count,
                SubQueryCount = subQueries.Count,
                MaxHopsExplored = maxHopsExplored,
                Duration = stopwatch.Elapsed
            }
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueryEntity>> ExtractQueryEntitiesAsync(
        string query,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var entities = new List<QueryEntity>();

        // Pattern 1: Quoted strings are likely entity names
        var quotedPattern = new Regex(@"""([^""]+)""|'([^']+)'");
        foreach (Match match in quotedPattern.Matches(query))
        {
            var entityName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (await EntityExistsAsync(entityName, userId, cancellationToken))
            {
                var importance = await _importancePropagator.GetEntityImportanceAsync(
                    entityName, userId, cancellationToken) ?? 0.5f;

                entities.Add(new QueryEntity
                {
                    Name = entityName,
                    ImportanceScore = importance,
                    Relation = EntityRelationType.Mentioned,
                    GraphDistance = 0,
                    RetrievalBoost = importance * 2.0f // Boost explicitly mentioned entities
                });
            }
        }

        // Pattern 2: Capitalized words/phrases
        var words = query.Split([' ', ',', '.', '?', '!', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        var i = 0;
        while (i < words.Length)
        {
            var word = words[i].Trim();

            // Skip stopwords and very short words
            if (word.Length < 2 || Stopwords.Contains(word))
            {
                i++;
                continue;
            }

            // Check for capitalized words (potential entities)
            if (char.IsUpper(word[0]) || (i == 0 && word.Length > 2))
            {
                // Try multi-word entity
                var candidate = word;
                var j = i + 1;
                while (j < words.Length && char.IsUpper(words[j][0]) && !Stopwords.Contains(words[j]))
                {
                    candidate += " " + words[j];
                    j++;
                }

                // Check if entity exists in graph
                if (await EntityExistsAsync(candidate, userId, cancellationToken))
                {
                    var importance = await _importancePropagator.GetEntityImportanceAsync(
                        candidate, userId, cancellationToken) ?? 0.5f;

                    entities.Add(new QueryEntity
                    {
                        Name = candidate,
                        ImportanceScore = importance,
                        Relation = EntityRelationType.Mentioned,
                        GraphDistance = 0,
                        RetrievalBoost = importance * 1.5f
                    });

                    i = j;
                    continue;
                }

                // Try single word
                if (candidate != word && await EntityExistsAsync(word, userId, cancellationToken))
                {
                    var importance = await _importancePropagator.GetEntityImportanceAsync(
                        word, userId, cancellationToken) ?? 0.5f;

                    entities.Add(new QueryEntity
                    {
                        Name = word,
                        ImportanceScore = importance,
                        Relation = EntityRelationType.Mentioned,
                        GraphDistance = 0,
                        RetrievalBoost = importance * 1.5f
                    });
                }
            }

            i++;
        }

        // Pattern 3: Match against known high-importance entities
        var topEntities = await _importancePropagator.GetTopEntitiesAsync(userId, 50, cancellationToken);
        foreach (var topEntity in topEntities)
        {
            if (query.Contains(topEntity.EntityName, StringComparison.OrdinalIgnoreCase))
            {
                if (!entities.Any(e => e.Name.Equals(topEntity.EntityName, StringComparison.OrdinalIgnoreCase)))
                {
                    entities.Add(new QueryEntity
                    {
                        Name = topEntity.EntityName,
                        ImportanceScore = topEntity.Score,
                        Relation = EntityRelationType.Implied,
                        GraphDistance = 0,
                        RetrievalBoost = topEntity.Score * 1.2f
                    });
                }
            }
        }

        // Deduplicate
        return entities
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.RetrievalBoost).First())
            .OrderByDescending(e => e.RetrievalBoost)
            .ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphSubQuery>> GenerateSubQueriesAsync(
        string query,
        IReadOnlyList<QueryEntity> entities,
        SubQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SubQueryOptions();
        var subQueries = new List<GraphSubQuery>();

        // Generate entity fact queries
        foreach (var entity in entities.Take(3))
        {
            subQueries.Add(new GraphSubQuery
            {
                Query = $"What facts are known about {entity.Name}?",
                Type = SubQueryType.EntityFacts,
                TargetEntities = [entity.Name],
                Priority = entity.ImportanceScore
            });
        }

        // Generate relationship queries for entity pairs
        if (options.IncludeRelationshipQueries && entities.Count >= 2)
        {
            for (var i = 0; i < Math.Min(entities.Count - 1, 2); i++)
            {
                for (var j = i + 1; j < Math.Min(entities.Count, 3); j++)
                {
                    subQueries.Add(new GraphSubQuery
                    {
                        Query = $"What is the relationship between {entities[i].Name} and {entities[j].Name}?",
                        Type = SubQueryType.EntityRelationship,
                        TargetEntities = [entities[i].Name, entities[j].Name],
                        Priority = (entities[i].ImportanceScore + entities[j].ImportanceScore) / 2
                    });
                }
            }
        }

        // Sort by priority and limit
        var result = subQueries
            .OrderByDescending(q => q.Priority)
            .Take(options.MaxSubQueries)
            .ToList();

        return Task.FromResult<IReadOnlyList<GraphSubQuery>>(result);
    }

    #region Helper Methods

    private async Task<bool> EntityExistsAsync(string entityName, string userId, CancellationToken cancellationToken)
    {
        var asSubject = await _entityStore.GetBySubjectAsync(entityName, userId, cancellationToken);
        if (asSubject?.Any() == true)
            return true;

        var asObject = await _entityStore.GetByObjectAsync(entityName, userId, cancellationToken);
        return asObject?.Any() == true;
    }

    private async Task<string?> GetCommunityContextAsync(
        IReadOnlyList<string> entityNames,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Assign entities to communities and get summary
            var communityIds = new HashSet<int>();
            foreach (var entity in entityNames)
            {
                var community = await _communityDetector.AssignToCommunityAsync(
                    Guid.Empty, // No specific memory
                    [entity],
                    cancellationToken);

                if (community >= 0)
                    communityIds.Add(community);
            }

            if (communityIds.Count == 0)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("Related topic clusters:");

            foreach (var communityId in communityIds.Take(2))
            {
                var summary = await _communityDetector.GetCommunitySummaryAsync(
                    communityId, userId, cancellationToken);

                sb.AppendLine(CultureInfo.InvariantCulture, $"- {summary.TopicLabel} ({summary.MemoryCount} memories)");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            LogFailedGetCommunityContext(_logger, ex);
            return null;
        }
    }

    private static string BuildExpandedQueryText(
        string originalQuery,
        IReadOnlyList<QueryEntity> mentionedEntities,
        List<QueryEntity> relatedEntities,
        List<EntityTriple> relevantFacts,
        string? communityContext,
        QueryExpansionOptions options)
    {
        var sb = new StringBuilder();
        var tokenEstimate = 0;

        // Original query
        sb.AppendLine("Query: " + originalQuery);
        sb.AppendLine();
        tokenEstimate += originalQuery.Length / 4;

        // Mentioned entities
        if (mentionedEntities.Count > 0)
        {
            sb.AppendLine("Key entities:");
            foreach (var entity in mentionedEntities)
            {
                var line = $"- {entity.Name} (importance: {entity.ImportanceScore:F2})";
                sb.AppendLine(line);
                tokenEstimate += line.Length / 4;

                if (tokenEstimate > options.MaxExpansionTokens * 0.3)
                    break;
            }
            sb.AppendLine();
        }

        // Related entities
        if (relatedEntities.Count > 0 && tokenEstimate < options.MaxExpansionTokens * 0.5)
        {
            sb.AppendLine("Related entities:");
            foreach (var entity in relatedEntities.Take(5))
            {
                var line = $"- {entity.Name} (distance: {entity.GraphDistance}, importance: {entity.ImportanceScore:F2})";
                sb.AppendLine(line);
                tokenEstimate += line.Length / 4;

                if (tokenEstimate > options.MaxExpansionTokens * 0.6)
                    break;
            }
            sb.AppendLine();
        }

        // Relevant facts
        if (relevantFacts.Count > 0 && tokenEstimate < options.MaxExpansionTokens * 0.8)
        {
            sb.AppendLine("Known facts:");
            foreach (var fact in relevantFacts.Take(5))
            {
                var line = $"- {fact.Subject} → {fact.Predicate} → {fact.ObjectValue}";
                sb.AppendLine(line);
                tokenEstimate += line.Length / 4;

                if (tokenEstimate > options.MaxExpansionTokens)
                    break;
            }
            sb.AppendLine();
        }

        // Community context
        if (!string.IsNullOrEmpty(communityContext) && tokenEstimate < options.MaxExpansionTokens)
        {
            sb.AppendLine(communityContext);
        }

        return sb.ToString();
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Expanding query for user {UserId}: '{Query}'")]
    private static partial void LogExpandingQueryUserUserIdQuery(ILogger logger, string userId, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Query expanded: {MentionedCount} mentioned, {RelatedCount} related, {FactCount} facts in {Duration}ms")]
    private static partial void LogQueryExpandedMentionedCountMentionedRelatedCount(ILogger logger, int mentionedCount, int relatedCount, int factCount, long duration);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to get community context")]
    private static partial void LogFailedGetCommunityContext(ILogger logger, Exception ex);
}
