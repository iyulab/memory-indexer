using System.Text;
using System.Text.RegularExpressions;
using MemoryIndexer.Sdk.Intelligence.KnowledgeGraph;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.EntityResolution;

/// <summary>
/// Rule-based coreference resolver using linguistic patterns and heuristics.
/// </summary>
public sealed partial class CoreferenceResolver : ICoreferenceResolver
{
    private readonly ILogger<CoreferenceResolver> _logger;

    // Pronoun patterns by gender and number
    private static readonly Dictionary<string, (GrammaticalGender Gender, GrammaticalNumber Number)> PronounInfo = new(StringComparer.OrdinalIgnoreCase)
    {
        // Personal pronouns
        ["he"] = (GrammaticalGender.Masculine, GrammaticalNumber.Singular),
        ["him"] = (GrammaticalGender.Masculine, GrammaticalNumber.Singular),
        ["she"] = (GrammaticalGender.Feminine, GrammaticalNumber.Singular),
        ["her"] = (GrammaticalGender.Feminine, GrammaticalNumber.Singular),
        ["it"] = (GrammaticalGender.Neuter, GrammaticalNumber.Singular),
        ["they"] = (GrammaticalGender.Animate, GrammaticalNumber.Plural),
        ["them"] = (GrammaticalGender.Animate, GrammaticalNumber.Plural),

        // Possessive pronouns
        ["his"] = (GrammaticalGender.Masculine, GrammaticalNumber.Singular),
        ["her"] = (GrammaticalGender.Feminine, GrammaticalNumber.Singular),
        ["hers"] = (GrammaticalGender.Feminine, GrammaticalNumber.Singular),
        ["its"] = (GrammaticalGender.Neuter, GrammaticalNumber.Singular),
        ["their"] = (GrammaticalGender.Animate, GrammaticalNumber.Plural),
        ["theirs"] = (GrammaticalGender.Animate, GrammaticalNumber.Plural),

        // Reflexive pronouns
        ["himself"] = (GrammaticalGender.Masculine, GrammaticalNumber.Singular),
        ["herself"] = (GrammaticalGender.Feminine, GrammaticalNumber.Singular),
        ["itself"] = (GrammaticalGender.Neuter, GrammaticalNumber.Singular),
        ["themselves"] = (GrammaticalGender.Animate, GrammaticalNumber.Plural)
    };

    // Entity type to gender mapping
    private static readonly Dictionary<EntityType, GrammaticalGender> EntityTypeGenders = new()
    {
        [EntityType.Person] = GrammaticalGender.Unknown, // Determined by name
        [EntityType.Organization] = GrammaticalGender.Neuter,
        [EntityType.Location] = GrammaticalGender.Neuter,
        [EntityType.Product] = GrammaticalGender.Neuter,
        [EntityType.Event] = GrammaticalGender.Neuter,
        [EntityType.Technical] = GrammaticalGender.Neuter,
        [EntityType.Concept] = GrammaticalGender.Neuter
    };

    // Common masculine names (subset for demo - production would use a larger dataset)
    private static readonly HashSet<string> MasculineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "james", "john", "robert", "michael", "william", "david", "richard", "joseph",
        "thomas", "charles", "daniel", "matthew", "anthony", "mark", "paul", "steven",
        "andrew", "joshua", "kevin", "brian", "edward", "ronald", "timothy", "jason"
    };

    // Common feminine names (subset for demo)
    private static readonly HashSet<string> FeminineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mary", "patricia", "jennifer", "linda", "elizabeth", "barbara", "susan", "jessica",
        "sarah", "karen", "nancy", "betty", "margaret", "sandra", "ashley", "dorothy",
        "kimberly", "emily", "donna", "michelle", "carol", "amanda", "melissa", "deborah"
    };

    public CoreferenceResolver(ILogger<CoreferenceResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<CoreferenceResult> ResolveAsync(
        string text,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new CoreferenceResult());
        }

        var entityList = entities.ToList();
        var result = new CoreferenceResult();
        var sentences = SplitSentences(text);
        var entityMentions = FindEntityMentions(text, entityList, sentences);
        var pronounMentions = FindPronounMentions(text, sentences);

        // Track entity chains
        var chains = new Dictionary<Guid, CoreferenceChain>();
        foreach (var entity in entityList)
        {
            chains[entity.Id] = new CoreferenceChain
            {
                ReferentEntity = entity,
                Mentions = entityMentions.Where(m => m.Type == MentionType.ProperName).ToList()
            };
        }

        // Resolve each pronoun
        foreach (var pronounMention in pronounMentions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (resolvedEntity, confidence, type) = ResolvePronoun(
                pronounMention, entityMentions, entityList, sentences);

            if (resolvedEntity != null)
            {
                var coreference = new Coreference
                {
                    Mention = pronounMention,
                    ReferentEntity = resolvedEntity,
                    Confidence = confidence,
                    Type = type,
                    Distance = CalculateDistance(pronounMention, entityMentions, resolvedEntity)
                };

                result.Coreferences.Add(coreference);

                if (chains.TryGetValue(resolvedEntity.Id, out var chain))
                {
                    chain.Mentions.Add(pronounMention);
                }
            }
            else
            {
                result.UnresolvedMentions.Add(new UnresolvedMention
                {
                    Mention = pronounMention,
                    Reason = "No suitable antecedent found",
                    CandidateEntities = GetCandidateEntities(pronounMention, entityList)
                });
            }
        }

        result.Chains.AddRange(chains.Values.Where(c => c.Mentions.Count > 0));

        _logger.LogDebug(
            "Resolved {ResolvedCount} coreferences, {UnresolvedCount} unresolved for {EntityCount} entities",
            result.ResolvedCount, result.UnresolvedCount, entityList.Count);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<CoreferenceResult> ResolveAcrossSegmentsAsync(
        IEnumerable<string> segments,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        var segmentList = segments.ToList();
        if (segmentList.Count == 0)
        {
            return new CoreferenceResult();
        }

        // Combine segments with markers
        var combinedText = new StringBuilder();
        var segmentOffsets = new List<int>();

        foreach (var segment in segmentList)
        {
            segmentOffsets.Add(combinedText.Length);
            combinedText.AppendLine(segment);
        }

        var result = await ResolveAsync(combinedText.ToString(), entities, cancellationToken);

        _logger.LogDebug(
            "Resolved coreferences across {SegmentCount} segments: {ResolvedCount} resolved",
            segmentList.Count, result.ResolvedCount);

        return result;
    }

    /// <inheritdoc />
    public string ExpandText(string text, CoreferenceResult coreferences, ExpansionOptions? options = null)
    {
        options ??= new ExpansionOptions();

        if (coreferences.Coreferences.Count == 0)
        {
            return text;
        }

        // Sort coreferences by position in reverse order (to avoid offset issues)
        var sortedCoreferences = coreferences.Coreferences
            .Where(c => c.Confidence >= options.MinConfidence)
            .OrderByDescending(c => c.Mention.StartPosition)
            .ToList();

        var result = new StringBuilder(text);

        foreach (var coref in sortedCoreferences)
        {
            var replacement = string.Format(
                options.ReplacementFormat,
                coref.ReferentEntity.Name,
                coref.Mention.Text);

            if (options.IncludeOriginalPronoun)
            {
                replacement = $"{coref.ReferentEntity.Name} ({coref.Mention.Text})";
            }

            result.Remove(coref.Mention.StartPosition, coref.Mention.EndPosition - coref.Mention.StartPosition);
            result.Insert(coref.Mention.StartPosition, replacement);
        }

        return result.ToString();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityMention>> GetAllMentionsAsync(
        string text,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        var entityList = entities.ToList();
        var sentences = SplitSentences(text);
        var result = new List<EntityMention>();

        // Get direct entity mentions
        result.AddRange(FindEntityMentions(text, entityList, sentences));

        // Get pronoun mentions with resolution
        var coreferences = await ResolveAsync(text, entityList, cancellationToken);
        result.AddRange(coreferences.Coreferences.Select(c => c.Mention));

        return result.OrderBy(m => m.StartPosition).ToList();
    }

    private List<string> SplitSentences(string text)
    {
        // Simple sentence splitter - production would use a proper NLP tokenizer
        return SentenceSplitRegex().Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
    }

    private List<EntityMention> FindEntityMentions(
        string text,
        List<Entity> entities,
        List<string> sentences)
    {
        var mentions = new List<EntityMention>();
        var textLower = text.ToLowerInvariant();

        foreach (var entity in entities)
        {
            var searchName = entity.Name.ToLowerInvariant();
            var index = 0;
            var isFirst = true;

            while ((index = textLower.IndexOf(searchName, index, StringComparison.Ordinal)) >= 0)
            {
                var sentenceIndex = GetSentenceIndex(index, text, sentences);

                mentions.Add(new EntityMention
                {
                    Text = text.Substring(index, entity.Name.Length),
                    StartPosition = index,
                    EndPosition = index + entity.Name.Length,
                    SentenceIndex = sentenceIndex,
                    Type = MentionType.ProperName,
                    Gender = InferGender(entity),
                    Number = GrammaticalNumber.Singular,
                    IsFirstMention = isFirst
                });

                isFirst = false;
                index += entity.Name.Length;
            }
        }

        return mentions.OrderBy(m => m.StartPosition).ToList();
    }

    private List<EntityMention> FindPronounMentions(string text, List<string> sentences)
    {
        var mentions = new List<EntityMention>();

        foreach (var match in PronounRegex().Matches(text).Cast<Match>())
        {
            var pronoun = match.Value;
            if (PronounInfo.TryGetValue(pronoun, out var info))
            {
                var sentenceIndex = GetSentenceIndex(match.Index, text, sentences);

                mentions.Add(new EntityMention
                {
                    Text = pronoun,
                    StartPosition = match.Index,
                    EndPosition = match.Index + match.Length,
                    SentenceIndex = sentenceIndex,
                    Type = GetMentionType(pronoun),
                    Gender = info.Gender,
                    Number = info.Number,
                    IsFirstMention = false
                });
            }
        }

        return mentions.OrderBy(m => m.StartPosition).ToList();
    }

    private (Entity? Entity, float Confidence, CoreferenceType Type) ResolvePronoun(
        EntityMention pronounMention,
        List<EntityMention> entityMentions,
        List<Entity> entities,
        List<string> sentences)
    {
        // Get candidate entities that precede this pronoun
        var precedingMentions = entityMentions
            .Where(m => m.StartPosition < pronounMention.StartPosition)
            .OrderByDescending(m => m.StartPosition)
            .ToList();

        if (precedingMentions.Count == 0)
        {
            return (null, 0, CoreferenceType.PersonalPronoun);
        }

        // Score each candidate
        var candidates = new List<(Entity Entity, float Score)>();

        foreach (var mention in precedingMentions)
        {
            var entity = entities.FirstOrDefault(e =>
                e.Name.Equals(mention.Text, StringComparison.OrdinalIgnoreCase));

            if (entity == null) continue;

            var score = CalculateCandidateScore(pronounMention, mention, entity, sentences);
            if (score > 0)
            {
                candidates.Add((entity, score));
            }
        }

        if (candidates.Count == 0)
        {
            return (null, 0, CoreferenceType.PersonalPronoun);
        }

        // Return best candidate
        var best = candidates.OrderByDescending(c => c.Score).First();
        return (best.Entity, best.Score, GetCoreferenceType(pronounMention.Text));
    }

    private float CalculateCandidateScore(
        EntityMention pronounMention,
        EntityMention candidateMention,
        Entity candidateEntity,
        List<string> sentences)
    {
        var score = 0.5f; // Base score

        // Gender agreement
        var entityGender = InferGender(candidateEntity);
        if (pronounMention.Gender != GrammaticalGender.Unknown &&
            entityGender != GrammaticalGender.Unknown)
        {
            if (IsGenderCompatible(pronounMention.Gender, entityGender))
            {
                score += 0.3f;
            }
            else
            {
                return 0; // Gender mismatch disqualifies
            }
        }

        // Number agreement
        var entityNumber = GetEntityNumber(candidateEntity);
        if (pronounMention.Number != GrammaticalNumber.Unknown &&
            entityNumber != GrammaticalNumber.Unknown)
        {
            if (pronounMention.Number == entityNumber)
            {
                score += 0.2f;
            }
            else
            {
                return 0; // Number mismatch disqualifies
            }
        }

        // Recency bonus (closer antecedents preferred)
        var sentenceDistance = pronounMention.SentenceIndex - candidateMention.SentenceIndex;
        if (sentenceDistance == 0)
        {
            score += 0.2f; // Same sentence
        }
        else if (sentenceDistance == 1)
        {
            score += 0.1f; // Adjacent sentence
        }
        else
        {
            score -= 0.05f * Math.Min(sentenceDistance - 1, 5); // Decay for distance
        }

        // Entity type bonus (Person entities more likely for he/she)
        if (candidateEntity.Type == EntityType.Person &&
            (pronounMention.Gender == GrammaticalGender.Masculine ||
             pronounMention.Gender == GrammaticalGender.Feminine))
        {
            score += 0.1f;
        }

        // Salience bonus (entities mentioned more often are more likely antecedents)
        score += Math.Min(0.1f, candidateEntity.OccurrenceCount * 0.02f);

        return Math.Min(1.0f, Math.Max(0, score));
    }

    private GrammaticalGender InferGender(Entity entity)
    {
        if (entity.Type == EntityType.Person)
        {
            var firstName = entity.Name.Split(' ')[0];
            if (MasculineNames.Contains(firstName))
            {
                return GrammaticalGender.Masculine;
            }
            if (FeminineNames.Contains(firstName))
            {
                return GrammaticalGender.Feminine;
            }
            return GrammaticalGender.Unknown;
        }

        return EntityTypeGenders.TryGetValue(entity.Type, out var gender)
            ? gender
            : GrammaticalGender.Unknown;
    }

    private bool IsGenderCompatible(GrammaticalGender pronounGender, GrammaticalGender entityGender)
    {
        if (pronounGender == GrammaticalGender.Unknown || entityGender == GrammaticalGender.Unknown)
        {
            return true;
        }

        if (pronounGender == GrammaticalGender.Animate)
        {
            return entityGender is GrammaticalGender.Masculine or GrammaticalGender.Feminine or GrammaticalGender.Animate;
        }

        return pronounGender == entityGender;
    }

    private GrammaticalNumber GetEntityNumber(Entity entity)
    {
        // Most entities are singular
        // Could be extended to check for plural forms
        return GrammaticalNumber.Singular;
    }

    private int GetSentenceIndex(int position, string text, List<string> sentences)
    {
        var currentPos = 0;
        for (int i = 0; i < sentences.Count; i++)
        {
            var nextPos = text.IndexOf(sentences[i], currentPos, StringComparison.Ordinal);
            if (nextPos >= 0 && position >= nextPos && position < nextPos + sentences[i].Length)
            {
                return i;
            }
            currentPos = nextPos + sentences[i].Length;
        }
        return sentences.Count - 1;
    }

    private static MentionType GetMentionType(string pronoun)
    {
        var lower = pronoun.ToLowerInvariant();
        if (lower is "his" or "her" or "hers" or "its" or "their" or "theirs")
        {
            return MentionType.Possessive;
        }
        if (lower is "this" or "that" or "these" or "those")
        {
            return MentionType.Demonstrative;
        }
        return MentionType.Pronoun;
    }

    private static CoreferenceType GetCoreferenceType(string pronoun)
    {
        var lower = pronoun.ToLowerInvariant();
        if (lower is "his" or "her" or "hers" or "its" or "their" or "theirs")
        {
            return CoreferenceType.PossessivePronoun;
        }
        if (lower is "himself" or "herself" or "itself" or "themselves")
        {
            return CoreferenceType.ReflexivePronoun;
        }
        if (lower is "this" or "that" or "these" or "those")
        {
            return CoreferenceType.DemonstrativePronoun;
        }
        return CoreferenceType.PersonalPronoun;
    }

    private int CalculateDistance(EntityMention mention, List<EntityMention> entityMentions, Entity entity)
    {
        var precedingMention = entityMentions
            .Where(m => m.StartPosition < mention.StartPosition &&
                       m.Text.Equals(entity.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.StartPosition)
            .FirstOrDefault();

        if (precedingMention == null)
        {
            return -1;
        }

        return mention.SentenceIndex - precedingMention.SentenceIndex;
    }

    private List<Entity> GetCandidateEntities(EntityMention mention, List<Entity> entities)
    {
        // Return entities that could potentially match based on gender/number
        return entities
            .Where(e => IsGenderCompatible(mention.Gender, InferGender(e)))
            .Take(3)
            .ToList();
    }

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.Compiled)]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\b(he|him|his|she|her|hers|it|its|they|them|their|theirs|himself|herself|itself|themselves)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PronounRegex();
}
