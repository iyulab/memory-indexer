using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.KnowledgeGraph;

/// <summary>
/// Extracts entities from text using pattern matching and heuristics.
/// </summary>
public sealed partial class EntityExtractor : IKnowledgeGraphService
{
    private readonly ILogger<EntityExtractor> _logger;

    // Common organization suffixes
    private static readonly HashSet<string> OrgSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inc", "Corp", "Ltd", "LLC", "Co", "Company", "Corporation",
        "Foundation", "Association", "Institute", "University", "College"
    };

    // Common person titles
    private static readonly HashSet<string> PersonTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr", "Mrs", "Ms", "Dr", "Prof", "CEO", "CTO", "CFO", "Manager", "Director"
    };

    // Technical terms indicators
    private static readonly HashSet<string> TechIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "API", "SDK", "CLI", "GUI", "URL", "HTTP", "HTTPS", "SQL", "JSON",
        "XML", "HTML", "CSS", "JavaScript", "Python", "Java", "C#", "NET",
        "Docker", "Kubernetes", "AWS", "Azure", "GCP", "database", "server",
        "algorithm", "framework", "library", "package", "module", "class",
        "function", "method", "interface", "protocol", "encryption"
    };

    // Common relation patterns
    private static readonly Dictionary<string, string> RelationPatterns = new()
    {
        { "works at|employed by|works for", "WORKS_AT" },
        { "located in|based in|located at", "LOCATED_IN" },
        { "part of|belongs to|member of", "PART_OF" },
        { "created by|developed by|built by|made by", "CREATED_BY" },
        { "manages|leads|heads|supervises", "MANAGES" },
        { "reports to|works under", "REPORTS_TO" },
        { "uses|utilizes|employs", "USES" },
        { "related to|connected to|associated with", "RELATED_TO" },
        { "scheduled for|planned for|set for", "SCHEDULED_FOR" },
        { "sent to|emailed to|messaged", "SENT_TO" }
    };

    public EntityExtractor(ILogger<EntityExtractor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Entity>> ExtractEntitiesAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        var entities = new List<Entity>();

        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult<IReadOnlyList<Entity>>(entities);

        _logger.LogDebug("Extracting entities from content of length {Length}", content.Length);

        // Extract different entity types
        entities.AddRange(ExtractEmails(content));
        entities.AddRange(ExtractUrls(content));
        entities.AddRange(ExtractDates(content));
        entities.AddRange(ExtractNumericValues(content));
        entities.AddRange(ExtractNamedEntities(content));
        entities.AddRange(ExtractTechnicalTerms(content));

        // Deduplicate entities
        var deduplicated = DeduplicateEntities(entities);

        _logger.LogDebug("Extracted {Count} unique entities", deduplicated.Count);

        return Task.FromResult<IReadOnlyList<Entity>>(deduplicated);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityRelation>> ExtractRelationsAsync(
        string content,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        var relations = new List<EntityRelation>();
        var entityList = entities.ToList();

        if (string.IsNullOrWhiteSpace(content) || entityList.Count < 2)
            return Task.FromResult<IReadOnlyList<EntityRelation>>(relations);

        _logger.LogDebug("Extracting relations from {EntityCount} entities", entityList.Count);

        // Extract relations using pattern matching
        foreach (var (pattern, relationType) in RelationPatterns)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var matches = regex.Matches(content);

            foreach (Match match in matches)
            {
                // Find entities near the relation pattern
                var nearbyEntities = FindNearbyEntities(content, match.Index, entityList);

                if (nearbyEntities.Count >= 2)
                {
                    relations.Add(new EntityRelation
                    {
                        Source = nearbyEntities[0],
                        Target = nearbyEntities[1],
                        RelationType = relationType,
                        Confidence = 0.7f,
                        Evidence = ExtractContext(content, match.Index, 50)
                    });
                }
            }
        }

        // Extract co-occurrence relations (entities mentioned together)
        var sentences = content.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var sentence in sentences)
        {
            var sentenceEntities = entityList
                .Where(e => sentence.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (var i = 0; i < sentenceEntities.Count - 1; i++)
            {
                for (var j = i + 1; j < sentenceEntities.Count; j++)
                {
                    // Check if relation already exists
                    if (!relations.Any(r =>
                        (r.Source.Name == sentenceEntities[i].Name && r.Target.Name == sentenceEntities[j].Name) ||
                        (r.Source.Name == sentenceEntities[j].Name && r.Target.Name == sentenceEntities[i].Name)))
                    {
                        relations.Add(new EntityRelation
                        {
                            Source = sentenceEntities[i],
                            Target = sentenceEntities[j],
                            RelationType = "MENTIONED_WITH",
                            Confidence = 0.5f,
                            IsBidirectional = true,
                            Evidence = sentence.Trim()
                        });
                    }
                }
            }
        }

        _logger.LogDebug("Extracted {Count} relations", relations.Count);

        return Task.FromResult<IReadOnlyList<EntityRelation>>(relations);
    }

    /// <inheritdoc />
    public async Task<KnowledgeGraph> BuildGraphAsync(
        IEnumerable<string> memoryContents,
        CancellationToken cancellationToken = default)
    {
        var graph = new KnowledgeGraph();
        var contentList = memoryContents.ToList();

        _logger.LogDebug("Building knowledge graph from {Count} contents", contentList.Count);

        foreach (var content in contentList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Extract entities
            var entities = await ExtractEntitiesAsync(content, cancellationToken);

            // Add entities to graph
            foreach (var entity in entities)
            {
                if (graph.NameIndex.TryGetValue(entity.NormalizedName, out var existingIds))
                {
                    // Entity already exists, increment occurrence
                    var existingEntity = graph.Entities[existingIds[0]];
                    existingEntity.OccurrenceCount++;
                }
                else
                {
                    // New entity
                    graph.Entities[entity.Id] = entity;

                    // Update name index
                    if (!graph.NameIndex.ContainsKey(entity.NormalizedName))
                        graph.NameIndex[entity.NormalizedName] = [];
                    graph.NameIndex[entity.NormalizedName].Add(entity.Id);

                    // Update type index
                    if (!graph.TypeIndex.ContainsKey(entity.Type))
                        graph.TypeIndex[entity.Type] = [];
                    graph.TypeIndex[entity.Type].Add(entity.Id);

                    // Initialize adjacency list
                    graph.AdjacencyList[entity.Id] = [];
                }
            }

            // Extract and add relations
            var relations = await ExtractRelationsAsync(content, entities, cancellationToken);

            foreach (var relation in relations)
            {
                // Map to graph entities
                var sourceId = FindEntityId(graph, relation.Source.NormalizedName);
                var targetId = FindEntityId(graph, relation.Target.NormalizedName);

                if (sourceId.HasValue && targetId.HasValue)
                {
                    // Check for duplicate relation
                    var existingRelation = graph.Relations.FirstOrDefault(r =>
                        r.Source.Id == sourceId && r.Target.Id == targetId && r.RelationType == relation.RelationType);

                    if (existingRelation != null)
                    {
                        existingRelation.Weight += 1.0f;
                    }
                    else
                    {
                        var graphRelation = new EntityRelation
                        {
                            Source = graph.Entities[sourceId.Value],
                            Target = graph.Entities[targetId.Value],
                            RelationType = relation.RelationType,
                            Confidence = relation.Confidence,
                            Evidence = relation.Evidence,
                            IsBidirectional = relation.IsBidirectional
                        };

                        graph.Relations.Add(graphRelation);

                        // Update adjacency list
                        graph.AdjacencyList[sourceId.Value].Add((graphRelation, targetId.Value));
                        if (relation.IsBidirectional)
                        {
                            graph.AdjacencyList[targetId.Value].Add((graphRelation, sourceId.Value));
                        }
                    }
                }
            }
        }

        graph.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation("Built knowledge graph with {EntityCount} entities and {RelationCount} relations",
            graph.EntityCount, graph.RelationCount);

        return graph;
    }

    /// <inheritdoc />
    public KnowledgeGraphQueryResult QueryGraph(string query, KnowledgeGraph graph, int maxResults = 10)
    {
        var result = new KnowledgeGraphQueryResult();
        var queryTerms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Find matching entities
        foreach (var (name, entityIds) in graph.NameIndex)
        {
            var matchScore = CalculateMatchScore(name, queryTerms);
            if (matchScore > 0)
            {
                foreach (var entityId in entityIds)
                {
                    var entity = graph.Entities[entityId];
                    result.MatchedEntities.Add(entity);
                    result.RelevanceScores[entityId] = matchScore;
                }
            }
        }

        // Sort by relevance and limit
        result.MatchedEntities = result.MatchedEntities
            .OrderByDescending(e => result.RelevanceScores[e.Id])
            .Take(maxResults)
            .ToList();

        // Find related entities and relations
        var relatedEntityIds = new HashSet<Guid>();
        foreach (var entity in result.MatchedEntities)
        {
            if (graph.AdjacencyList.TryGetValue(entity.Id, out var neighbors))
            {
                foreach (var (relation, neighborId) in neighbors)
                {
                    result.RelatedRelations.Add(relation);
                    relatedEntityIds.Add(neighborId);
                }
            }
        }

        // Add related entities
        foreach (var entityId in relatedEntityIds)
        {
            if (!result.MatchedEntities.Any(e => e.Id == entityId))
            {
                result.RelatedEntities.Add(graph.Entities[entityId]);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public KnowledgeGraph MergeGraphs(KnowledgeGraph graph1, KnowledgeGraph graph2)
    {
        var merged = new KnowledgeGraph
        {
            Entities = new Dictionary<Guid, Entity>(graph1.Entities),
            Relations = new List<EntityRelation>(graph1.Relations),
            AdjacencyList = new Dictionary<Guid, List<(EntityRelation, Guid)>>(
                graph1.AdjacencyList.ToDictionary(x => x.Key, x => new List<(EntityRelation, Guid)>(x.Value))),
            NameIndex = new Dictionary<string, List<Guid>>(
                graph1.NameIndex.ToDictionary(x => x.Key, x => new List<Guid>(x.Value))),
            TypeIndex = new Dictionary<EntityType, List<Guid>>(
                graph1.TypeIndex.ToDictionary(x => x.Key, x => new List<Guid>(x.Value)))
        };

        // Merge entities from graph2
        foreach (var (id, entity) in graph2.Entities)
        {
            if (merged.NameIndex.TryGetValue(entity.NormalizedName, out var existingIds))
            {
                // Entity already exists, merge metadata
                var existingEntity = merged.Entities[existingIds[0]];
                existingEntity.OccurrenceCount += entity.OccurrenceCount;
                existingEntity.SourceMemoryIds.AddRange(entity.SourceMemoryIds);
            }
            else
            {
                // New entity
                merged.Entities[id] = entity;
                merged.NameIndex[entity.NormalizedName] = [id];

                if (!merged.TypeIndex.ContainsKey(entity.Type))
                    merged.TypeIndex[entity.Type] = [];
                merged.TypeIndex[entity.Type].Add(id);

                merged.AdjacencyList[id] = [];
            }
        }

        // Merge relations from graph2
        foreach (var relation in graph2.Relations)
        {
            var sourceId = FindEntityId(merged, relation.Source.NormalizedName);
            var targetId = FindEntityId(merged, relation.Target.NormalizedName);

            if (sourceId.HasValue && targetId.HasValue)
            {
                var existingRelation = merged.Relations.FirstOrDefault(r =>
                    r.Source.Id == sourceId && r.Target.Id == targetId && r.RelationType == relation.RelationType);

                if (existingRelation != null)
                {
                    existingRelation.Weight += relation.Weight;
                }
                else
                {
                    var newRelation = new EntityRelation
                    {
                        Source = merged.Entities[sourceId.Value],
                        Target = merged.Entities[targetId.Value],
                        RelationType = relation.RelationType,
                        Confidence = relation.Confidence,
                        Evidence = relation.Evidence,
                        IsBidirectional = relation.IsBidirectional,
                        Weight = relation.Weight
                    };

                    merged.Relations.Add(newRelation);
                    merged.AdjacencyList[sourceId.Value].Add((newRelation, targetId.Value));

                    if (relation.IsBidirectional)
                    {
                        merged.AdjacencyList[targetId.Value].Add((newRelation, sourceId.Value));
                    }
                }
            }
        }

        merged.UpdatedAt = DateTime.UtcNow;
        return merged;
    }

    private static IEnumerable<Entity> ExtractEmails(string content)
    {
        var matches = EmailRegex().Matches(content);
        return matches.Select(m => new Entity
        {
            Name = m.Value,
            NormalizedName = m.Value.ToLowerInvariant(),
            Type = EntityType.Email,
            Confidence = 1.0f
        });
    }

    private static IEnumerable<Entity> ExtractUrls(string content)
    {
        var matches = UrlRegex().Matches(content);
        return matches.Select(m => new Entity
        {
            Name = m.Value,
            NormalizedName = m.Value.ToLowerInvariant(),
            Type = EntityType.Url,
            Confidence = 1.0f
        });
    }

    private static IEnumerable<Entity> ExtractDates(string content)
    {
        var entities = new List<Entity>();

        // ISO dates
        var isoMatches = IsoDateRegex().Matches(content);
        entities.AddRange(isoMatches.Select(m => new Entity
        {
            Name = m.Value,
            NormalizedName = m.Value,
            Type = EntityType.DateTime,
            Confidence = 1.0f
        }));

        // US dates
        var usMatches = UsDateRegex().Matches(content);
        entities.AddRange(usMatches.Select(m => new Entity
        {
            Name = m.Value,
            NormalizedName = m.Value,
            Type = EntityType.DateTime,
            Confidence = 0.9f
        }));

        // Time patterns
        var timeMatches = TimeRegex().Matches(content);
        entities.AddRange(timeMatches.Select(m => new Entity
        {
            Name = m.Value,
            NormalizedName = m.Value,
            Type = EntityType.DateTime,
            Confidence = 0.9f
        }));

        return entities;
    }

    private static IEnumerable<Entity> ExtractNumericValues(string content)
    {
        var entities = new List<Entity>();

        // Currency values
        var currencyMatches = CurrencyRegex().Matches(content);
        entities.AddRange(currencyMatches.Select(m => new Entity
        {
            Name = m.Value,
            NormalizedName = m.Value,
            Type = EntityType.Numeric,
            Confidence = 1.0f,
            Metadata = { ["subtype"] = "currency" }
        }));

        // Percentages
        var percentMatches = PercentRegex().Matches(content);
        entities.AddRange(percentMatches.Select(m => new Entity
        {
            Name = m.Value,
            NormalizedName = m.Value,
            Type = EntityType.Numeric,
            Confidence = 1.0f,
            Metadata = { ["subtype"] = "percentage" }
        }));

        return entities;
    }

    private static IEnumerable<Entity> ExtractNamedEntities(string content)
    {
        var entities = new List<Entity>();

        // Capitalized word sequences (potential names or organizations)
        var matches = CapitalizedSequenceRegex().Matches(content);

        foreach (Match match in matches)
        {
            var name = match.Value.Trim();
            if (name.Length < 2) continue;

            var words = name.Split(' ');
            var type = ClassifyNamedEntity(words);

            entities.Add(new Entity
            {
                Name = name,
                NormalizedName = name.ToLowerInvariant(),
                Type = type,
                Confidence = type == EntityType.Unknown ? 0.5f : 0.8f
            });
        }

        return entities;
    }

    private static IEnumerable<Entity> ExtractTechnicalTerms(string content)
    {
        var entities = new List<Entity>();

        foreach (var term in TechIndicators)
        {
            if (content.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                entities.Add(new Entity
                {
                    Name = term,
                    NormalizedName = term.ToLowerInvariant(),
                    Type = EntityType.Technical,
                    Confidence = 0.9f
                });
            }
        }

        return entities;
    }

    private static EntityType ClassifyNamedEntity(string[] words)
    {
        // Check for organization indicators
        if (words.Any(w => OrgSuffixes.Contains(w)))
            return EntityType.Organization;

        // Check for person titles
        if (words.Length > 1 && PersonTitles.Contains(words[0]))
            return EntityType.Person;

        // Check for location indicators
        var lastWord = words.Last().ToLowerInvariant();
        if (lastWord is "city" or "state" or "country" or "street" or "avenue" or "road")
            return EntityType.Location;

        // Two-word sequences that look like names
        if (words.Length == 2 && words.All(w => w.Length > 1 && char.IsUpper(w[0])))
            return EntityType.Person;

        return EntityType.Unknown;
    }

    private static List<Entity> DeduplicateEntities(List<Entity> entities)
    {
        var unique = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var key = entity.NormalizedName ?? entity.Name.ToLowerInvariant();
            if (unique.TryGetValue(key, out var existing))
            {
                existing.OccurrenceCount++;
            }
            else
            {
                unique[key] = entity;
            }
        }

        return unique.Values.ToList();
    }

    private static List<Entity> FindNearbyEntities(string content, int position, List<Entity> entities)
    {
        const int windowSize = 100;
        var start = Math.Max(0, position - windowSize);
        var end = Math.Min(content.Length, position + windowSize);
        var window = content.Substring(start, end - start);

        return entities
            .Where(e => window.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => Math.Abs(window.IndexOf(e.Name, StringComparison.OrdinalIgnoreCase) - windowSize / 2))
            .Take(2)
            .ToList();
    }

    private static string ExtractContext(string content, int position, int contextSize)
    {
        var start = Math.Max(0, position - contextSize);
        var end = Math.Min(content.Length, position + contextSize);
        return content.Substring(start, end - start).Trim();
    }

    private static Guid? FindEntityId(KnowledgeGraph graph, string normalizedName)
    {
        return graph.NameIndex.TryGetValue(normalizedName, out var ids) && ids.Count > 0
            ? ids[0]
            : null;
    }

    private static float CalculateMatchScore(string entityName, string[] queryTerms)
    {
        var entityLower = entityName.ToLowerInvariant();
        var score = 0f;

        foreach (var term in queryTerms)
        {
            if (entityLower.Contains(term))
            {
                score += entityLower == term ? 1.0f : 0.5f;
            }
        }

        return score;
    }

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"https?://[^\s<>\""]+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\d{1,2}/\d{1,2}/\d{2,4}")]
    private static partial Regex UsDateRegex();

    [GeneratedRegex(@"\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AP]M)?")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"\$[\d,]+(?:\.\d{2})?|\d+(?:,\d{3})*(?:\.\d{2})?\s*(?:USD|EUR|GBP)")]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex(@"\d+(?:\.\d+)?%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"(?:[A-Z][a-z]+\s*)+")]
    private static partial Regex CapitalizedSequenceRegex();
}
