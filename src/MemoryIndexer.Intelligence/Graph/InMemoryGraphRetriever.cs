using System.Diagnostics;
using System.Text;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Intelligence.KnowledgeGraph;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Graph;

/// <summary>
/// In-memory implementation of graph-based retrieval with multi-hop traversal.
/// Uses EntityTriples as the underlying knowledge representation.
/// </summary>
/// <remarks>
/// Research basis: Combines Graphiti's temporal graph traversal with LightRAG's
/// entity-relation retrieval patterns.
/// </remarks>
public sealed class InMemoryGraphRetriever : IGraphRetriever
{
    private readonly ITemporalEntityStore _tripleStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<InMemoryGraphRetriever> _logger;

    // In-memory indices for efficient graph operations
    private readonly Dictionary<string, HashSet<Guid>> _subjectIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<Guid>> _objectIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<Guid>> _predicateIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexLock = new();

    public InMemoryGraphRetriever(
        ITemporalEntityStore tripleStore,
        IEmbeddingService embeddingService,
        IMemoryStore memoryStore,
        ILogger<InMemoryGraphRetriever> logger)
    {
        _tripleStore = tripleStore;
        _embeddingService = embeddingService;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GraphTraversalResult> TraverseAsync(
        string startEntity,
        int maxHops = 2,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GraphTraversalOptions();
        var stopwatch = Stopwatch.StartNew();

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredEntities = new List<DiscoveredEntity>();
        var traversedRelations = new List<TraversedRelation>();
        var currentFrontier = new List<(string Entity, int Depth, float Score)> { (startEntity, 0, 1.0f) };

        _logger.LogDebug("Starting graph traversal from '{Entity}' with max {MaxHops} hops",
            startEntity, maxHops);

        while (currentFrontier.Count > 0 && discoveredEntities.Count < options.MaxEntities)
        {
            var nextFrontier = new List<(string Entity, int Depth, float Score)>();

            foreach (var (entity, depth, score) in currentFrontier)
            {
                if (visited.Contains(entity) || depth > maxHops)
                    continue;

                visited.Add(entity);

                // Get all facts for this entity
                var facts = await GetEntityFactsInternalAsync(entity, options, cancellationToken);

                // Add discovered entity
                discoveredEntities.Add(new DiscoveredEntity
                {
                    Name = entity,
                    HopDistance = depth,
                    RelevanceScore = score * (1.0f - (depth * 0.2f)), // Decay with distance
                    Facts = facts,
                    Type = InferEntityType(entity, facts)
                });

                // Expand to neighbors
                if (depth < maxHops)
                {
                    foreach (var fact in facts)
                    {
                        // Filter by relation type if specified
                        if (options.IncludeRelationTypes != null &&
                            !options.IncludeRelationTypes.Contains(fact.Predicate))
                            continue;

                        if (options.ExcludeRelationTypes != null &&
                            options.ExcludeRelationTypes.Contains(fact.Predicate))
                            continue;

                        // Filter by confidence
                        if (fact.Confidence < options.MinConfidence)
                            continue;

                        // Determine neighbor entity
                        string? neighbor = null;
                        if (entity.Equals(fact.Subject, StringComparison.OrdinalIgnoreCase))
                        {
                            neighbor = fact.ObjectValue;
                        }
                        else if (entity.Equals(fact.ObjectValue, StringComparison.OrdinalIgnoreCase))
                        {
                            neighbor = fact.Subject;
                        }

                        if (neighbor != null && !visited.Contains(neighbor))
                        {
                            nextFrontier.Add((neighbor, depth + 1, score * fact.Confidence));

                            traversedRelations.Add(new TraversedRelation
                            {
                                FromEntity = fact.Subject,
                                ToEntity = fact.ObjectValue,
                                RelationType = fact.Predicate,
                                Confidence = fact.Confidence,
                                HopLevel = depth + 1
                            });
                        }
                    }
                }
            }

            currentFrontier = nextFrontier
                .GroupBy(x => x.Entity, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.First().Depth, g.Max(x => x.Score)))
                .Take(options.MaxEntities - discoveredEntities.Count)
                .ToList();
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Graph traversal completed: {Entities} entities, {Relations} relations in {Duration}ms",
            discoveredEntities.Count, traversedRelations.Count, stopwatch.ElapsedMilliseconds);

        return new GraphTraversalResult
        {
            StartEntity = startEntity,
            DiscoveredEntities = discoveredEntities,
            TraversedRelations = traversedRelations,
            Statistics = new TraversalStatistics
            {
                EntitiesVisited = discoveredEntities.Count,
                RelationsTraversed = traversedRelations.Count,
                MaxDepthReached = discoveredEntities.Count > 0
                    ? discoveredEntities.Max(e => e.HopDistance)
                    : 0,
                Duration = stopwatch.Elapsed
            }
        };
    }

    /// <inheritdoc />
    public async Task<GraphPathResult?> FindPathAsync(
        string fromEntity,
        string toEntity,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GraphTraversalOptions { MaxHops = 5 };

        // BFS for shortest path
        var visited = new Dictionary<string, (string? Parent, TraversedRelation? Relation)>(
            StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        queue.Enqueue(fromEntity);
        visited[fromEntity] = (null, null);

        _logger.LogDebug("Finding path from '{From}' to '{To}'", fromEntity, toEntity);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.Equals(toEntity, StringComparison.OrdinalIgnoreCase))
            {
                // Found path - reconstruct
                return ReconstructPath(fromEntity, toEntity, visited);
            }

            // Get path depth
            var depth = GetPathDepth(current, fromEntity, visited);
            if (depth >= options.MaxHops)
                continue;

            // Get neighbors
            var facts = await GetEntityFactsInternalAsync(current, options, cancellationToken);

            foreach (var fact in facts)
            {
                if (fact.Confidence < options.MinConfidence)
                    continue;

                string? neighbor = null;
                if (current.Equals(fact.Subject, StringComparison.OrdinalIgnoreCase))
                    neighbor = fact.ObjectValue;
                else if (current.Equals(fact.ObjectValue, StringComparison.OrdinalIgnoreCase))
                    neighbor = fact.Subject;

                if (neighbor != null && !visited.ContainsKey(neighbor))
                {
                    visited[neighbor] = (current, new TraversedRelation
                    {
                        FromEntity = fact.Subject,
                        ToEntity = fact.ObjectValue,
                        RelationType = fact.Predicate,
                        Confidence = fact.Confidence,
                        HopLevel = depth + 1
                    });
                    queue.Enqueue(neighbor);
                }
            }
        }

        _logger.LogDebug("No path found from '{From}' to '{To}'", fromEntity, toEntity);
        return null;
    }

    /// <inheritdoc />
    public async Task<EntityFactsResult> GetEntityFactsAsync(
        string entityName,
        EntityQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EntityQueryOptions();

        var subjectFacts = new List<EntityTriple>();
        var objectFacts = new List<EntityTriple>();
        var relatedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var predicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get facts where entity is subject
        if (options.IncludeAsSubject)
        {
            var asSubject = await _tripleStore.GetBySubjectAsync(entityName, options.UserId, cancellationToken);

            foreach (var fact in asSubject)
            {
                if (!ShouldIncludeFact(fact, options))
                    continue;

                subjectFacts.Add(fact);
                predicates.Add(fact.Predicate);
                relatedEntities.Add(fact.ObjectValue);
            }
        }

        // Get facts where entity is object
        if (options.IncludeAsObject)
        {
            var asObject = await _tripleStore.GetByObjectAsync(entityName, options.UserId, cancellationToken);

            foreach (var fact in asObject)
            {
                if (!ShouldIncludeFact(fact, options))
                    continue;

                objectFacts.Add(fact);
                predicates.Add(fact.Predicate);
                relatedEntities.Add(fact.Subject);
            }
        }

        // Apply limit
        if (subjectFacts.Count + objectFacts.Count > options.MaxFacts)
        {
            var total = subjectFacts.Concat(objectFacts)
                .OrderByDescending(f => f.Confidence)
                .Take(options.MaxFacts)
                .ToList();

            subjectFacts = total.Where(f => f.Subject.Equals(entityName, StringComparison.OrdinalIgnoreCase)).ToList();
            objectFacts = total.Where(f => f.ObjectValue.Equals(entityName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return new EntityFactsResult
        {
            EntityName = entityName,
            SubjectFacts = subjectFacts,
            ObjectFacts = objectFacts,
            UniquePredicates = predicates.ToList(),
            RelatedEntities = relatedEntities.ToList()
        };
    }

    /// <inheritdoc />
    public async Task<HybridGraphResult> HybridRetrieveAsync(
        string query,
        string userId,
        HybridGraphOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new HybridGraphOptions();

        _logger.LogDebug("Hybrid retrieval for query: '{Query}'", query);

        // Step 1: Semantic search for relevant memories
        var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var searchOptions = new MemorySearchOptions
        {
            UserId = userId,
            Limit = options.SemanticTopK,
            MinScore = options.MinSemanticScore
        };

        var searchResults = await _memoryStore.SearchAsync(embedding, searchOptions, cancellationToken);
        var semanticResults = searchResults.Select(r => r.Memory).ToList();

        // Step 2: Extract entities from query and results
        var queryEntities = ExtractEntitiesFromText(query);
        var resultEntities = semanticResults
            .SelectMany(r => r.Entities)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allEntities = queryEntities.Concat(resultEntities)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Step 3: Graph expansion
        var graphContext = new List<EntityTriple>();
        var scoredEntities = new List<ScoredEntity>();

        foreach (var entity in allEntities)
        {
            var traversal = await TraverseAsync(
                entity,
                options.GraphExpansionHops,
                new GraphTraversalOptions { UserId = userId },
                cancellationToken);

            foreach (var discovered in traversal.DiscoveredEntities)
            {
                graphContext.AddRange(discovered.Facts);
                scoredEntities.Add(new ScoredEntity
                {
                    Name = discovered.Name,
                    Score = discovered.RelevanceScore,
                    Type = discovered.Type,
                    FactCount = discovered.Facts.Count
                });
            }
        }

        // Deduplicate and score entities
        scoredEntities = scoredEntities
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ScoredEntity
            {
                Name = g.Key,
                Score = g.Max(e => e.Score),
                Type = g.First().Type,
                FactCount = g.Sum(e => e.FactCount)
            })
            .OrderByDescending(e => e.Score)
            .Take(options.SemanticTopK)
            .ToList();

        // Deduplicate graph context
        graphContext = graphContext
            .GroupBy(f => f.Id)
            .Select(g => g.First())
            .OrderByDescending(f => f.Confidence)
            .ToList();

        // Format context for LLM
        var formattedContext = options.IncludeGraphContext
            ? FormatGraphContext(scoredEntities, graphContext)
            : string.Empty;

        return new HybridGraphResult
        {
            Query = query,
            SemanticResults = semanticResults,
            GraphContext = graphContext,
            RelevantEntities = scoredEntities,
            FormattedContext = formattedContext
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredEntity>> GetRelevantEntitiesAsync(
        string query,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        // Extract entities from query
        var queryEntities = ExtractEntitiesFromText(query);

        // Get all unique entities from the store
        var allTriples = await _tripleStore.GetAllActiveAsync(null, cancellationToken);
        var entityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var triple in allTriples)
        {
            entityCounts.TryGetValue(triple.Subject, out var subjCount);
            entityCounts[triple.Subject] = subjCount + 1;

            entityCounts.TryGetValue(triple.ObjectValue, out var objCount);
            entityCounts[triple.ObjectValue] = objCount + 1;
        }

        // Score entities based on query match and connectivity
        var scored = new List<ScoredEntity>();

        foreach (var (entity, count) in entityCounts)
        {
            var score = 0f;

            // Boost for direct query mention
            if (queryEntities.Any(qe => entity.Contains(qe, StringComparison.OrdinalIgnoreCase) ||
                                        qe.Contains(entity, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.5f;
            }

            // Score based on connectivity (normalized log)
            score += (float)(Math.Log(1 + count) / 10);

            scored.Add(new ScoredEntity
            {
                Name = entity,
                Score = Math.Min(score, 1.0f),
                FactCount = count
            });
        }

        return scored
            .OrderByDescending(e => e.Score)
            .Take(topK)
            .ToList();
    }

    #region Helper Methods

    private async Task<IReadOnlyList<EntityTriple>> GetEntityFactsInternalAsync(
        string entity,
        GraphTraversalOptions options,
        CancellationToken cancellationToken)
    {
        var asSubject = await _tripleStore.GetBySubjectAsync(entity, options.UserId, cancellationToken);
        var asObject = await _tripleStore.GetByObjectAsync(entity, options.UserId, cancellationToken);

        var all = asSubject.Concat(asObject);

        if (options.FilterByTemporalValidity)
        {
            var asOf = options.AsOfDate ?? DateTime.UtcNow;
            all = all.Where(f => f.WasValidAt(asOf));
        }

        return all.ToList();
    }

    private static bool ShouldIncludeFact(EntityTriple fact, EntityQueryOptions options)
    {
        if (options.CurrentOnly && !fact.IsCurrentlyValid)
            return false;

        if (options.AsOfDate.HasValue && !fact.WasValidAt(options.AsOfDate.Value))
            return false;

        if (options.PredicateFilter != null && !options.PredicateFilter.Contains(fact.Predicate))
            return false;

        return true;
    }

    private static int GetPathDepth(
        string current,
        string start,
        Dictionary<string, (string? Parent, TraversedRelation? Relation)> visited)
    {
        var depth = 0;
        var node = current;

        while (node != null && !node.Equals(start, StringComparison.OrdinalIgnoreCase))
        {
            if (visited.TryGetValue(node, out var info))
            {
                node = info.Parent;
                depth++;
            }
            else
            {
                break;
            }
        }

        return depth;
    }

    private static GraphPathResult ReconstructPath(
        string from,
        string to,
        Dictionary<string, (string? Parent, TraversedRelation? Relation)> visited)
    {
        var pathEntities = new List<string>();
        var pathRelations = new List<TraversedRelation>();

        var current = to;
        while (current != null)
        {
            pathEntities.Add(current);

            if (visited.TryGetValue(current, out var info))
            {
                if (info.Relation != null)
                    pathRelations.Add(info.Relation);
                current = info.Parent;
            }
            else
            {
                break;
            }
        }

        pathEntities.Reverse();
        pathRelations.Reverse();

        return new GraphPathResult
        {
            FromEntity = from,
            ToEntity = to,
            PathEntities = pathEntities,
            PathRelations = pathRelations
        };
    }

    private static EntityType? InferEntityType(string entityName, IReadOnlyList<EntityTriple> facts)
    {
        // Simple heuristic-based type inference
        var lowerName = entityName.ToLowerInvariant();

        if (lowerName.Contains("@") || lowerName.Contains("email"))
            return EntityType.Email;

        if (facts.Any(f => f.Predicate.Contains("located", StringComparison.OrdinalIgnoreCase) ||
                          f.Predicate.Contains("lives", StringComparison.OrdinalIgnoreCase)))
            return EntityType.Location;

        if (facts.Any(f => f.Predicate.Contains("works", StringComparison.OrdinalIgnoreCase) ||
                          f.Predicate.Contains("employed", StringComparison.OrdinalIgnoreCase)))
            return EntityType.Organization;

        if (facts.Any(f => f.Predicate.Contains("born", StringComparison.OrdinalIgnoreCase) ||
                          f.Predicate.Contains("age", StringComparison.OrdinalIgnoreCase)))
            return EntityType.Person;

        return null;
    }

    private static List<string> ExtractEntitiesFromText(string text)
    {
        // Simple entity extraction (proper nouns, capitalized words)
        var entities = new List<string>();
        var words = text.Split([' ', ',', '.', '?', '!', ';', ':'], StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i].Trim();

            // Skip short words and common words
            if (word.Length < 2 || IsCommonWord(word))
                continue;

            // Check for capitalized words (potential entities)
            if (char.IsUpper(word[0]) && i > 0) // Skip first word as it's always capitalized
            {
                // Check for multi-word entities
                var entity = word;
                while (i + 1 < words.Length && char.IsUpper(words[i + 1][0]) && !IsCommonWord(words[i + 1]))
                {
                    i++;
                    entity += " " + words[i];
                }
                entities.Add(entity);
            }
        }

        return entities.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsCommonWord(string word)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "must", "can", "and", "or", "but", "if",
            "then", "else", "when", "where", "why", "how", "what", "which", "who",
            "I", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us",
            "them", "my", "your", "his", "its", "our", "their", "this", "that"
        };
        return common.Contains(word);
    }

    private static string FormatGraphContext(
        IReadOnlyList<ScoredEntity> entities,
        IReadOnlyList<EntityTriple> facts)
    {
        var sb = new StringBuilder();

        if (entities.Count > 0)
        {
            sb.AppendLine("## Related Entities");
            foreach (var entity in entities.Take(10))
            {
                var typeStr = entity.Type.HasValue ? $" ({entity.Type})" : "";
                sb.AppendLine($"- {entity.Name}{typeStr}: {entity.FactCount} facts");
            }
            sb.AppendLine();
        }

        if (facts.Count > 0)
        {
            sb.AppendLine("## Knowledge Graph Facts");
            foreach (var fact in facts.Take(20))
            {
                sb.AppendLine($"- {fact.Subject} → {fact.Predicate} → {fact.ObjectValue}");
            }
        }

        return sb.ToString();
    }

    #endregion
}
