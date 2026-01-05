using MemoryIndexer.Sdk.Intelligence.KnowledgeGraph;

namespace MemoryIndexer.Sdk.Intelligence.EntityResolution;

/// <summary>
/// Service for resolving coreferences (pronouns and anaphoric expressions) to their referent entities.
/// </summary>
public interface ICoreferenceResolver
{
    /// <summary>
    /// Resolves coreferences in text, linking pronouns and anaphoric expressions to their referent entities.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <param name="entities">Known entities in the text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved coreferences.</returns>
    Task<CoreferenceResult> ResolveAsync(
        string text,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves coreferences across multiple segments of text (e.g., multi-turn conversation).
    /// </summary>
    /// <param name="segments">Text segments in chronological order.</param>
    /// <param name="entities">Known entities across all segments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved coreferences across all segments.</returns>
    Task<CoreferenceResult> ResolveAcrossSegmentsAsync(
        IEnumerable<string> segments,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Expands text by replacing pronouns with their referent entity names.
    /// </summary>
    /// <param name="text">Original text with pronouns.</param>
    /// <param name="coreferences">Resolved coreferences.</param>
    /// <param name="options">Expansion options.</param>
    /// <returns>Expanded text with pronouns replaced.</returns>
    string ExpandText(string text, CoreferenceResult coreferences, ExpansionOptions? options = null);

    /// <summary>
    /// Gets entity mentions including both explicit mentions and coreferences.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <param name="entities">Known entities.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All entity mentions with their positions.</returns>
    Task<IReadOnlyList<EntityMention>> GetAllMentionsAsync(
        string text,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of coreference resolution.
/// </summary>
public sealed class CoreferenceResult
{
    /// <summary>
    /// All coreference chains found in the text.
    /// </summary>
    public List<CoreferenceChain> Chains { get; init; } = [];

    /// <summary>
    /// Individual coreference links (mention → entity).
    /// </summary>
    public List<Coreference> Coreferences { get; init; } = [];

    /// <summary>
    /// Mentions that could not be resolved.
    /// </summary>
    public List<UnresolvedMention> UnresolvedMentions { get; init; } = [];

    /// <summary>
    /// Total number of resolved coreferences.
    /// </summary>
    public int ResolvedCount => Coreferences.Count;

    /// <summary>
    /// Total number of unresolved mentions.
    /// </summary>
    public int UnresolvedCount => UnresolvedMentions.Count;

    /// <summary>
    /// Resolution rate (resolved / total mentions).
    /// </summary>
    public float ResolutionRate =>
        (ResolvedCount + UnresolvedCount) > 0
            ? (float)ResolvedCount / (ResolvedCount + UnresolvedCount)
            : 1.0f;
}

/// <summary>
/// A chain of coreferent mentions all referring to the same entity.
/// </summary>
public sealed class CoreferenceChain
{
    /// <summary>
    /// The canonical entity this chain refers to.
    /// </summary>
    public required Entity ReferentEntity { get; init; }

    /// <summary>
    /// All mentions in this chain (including the original entity mention).
    /// </summary>
    public List<EntityMention> Mentions { get; init; } = [];

    /// <summary>
    /// Confidence score for the chain (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; init; } = 1.0f;
}

/// <summary>
/// A single coreference link between a mention and its referent entity.
/// </summary>
public sealed class Coreference
{
    /// <summary>
    /// The mention (pronoun or anaphoric expression).
    /// </summary>
    public required EntityMention Mention { get; init; }

    /// <summary>
    /// The entity this mention refers to.
    /// </summary>
    public required Entity ReferentEntity { get; init; }

    /// <summary>
    /// Confidence score for this resolution (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; init; } = 1.0f;

    /// <summary>
    /// Type of coreference.
    /// </summary>
    public CoreferenceType Type { get; init; }

    /// <summary>
    /// Distance between mention and antecedent (in sentences or tokens).
    /// </summary>
    public int Distance { get; init; }
}

/// <summary>
/// A mention of an entity in text.
/// </summary>
public sealed class EntityMention
{
    /// <summary>
    /// The text of the mention.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Start position in the original text.
    /// </summary>
    public int StartPosition { get; init; }

    /// <summary>
    /// End position in the original text.
    /// </summary>
    public int EndPosition { get; init; }

    /// <summary>
    /// Sentence index containing this mention (0-based).
    /// </summary>
    public int SentenceIndex { get; init; }

    /// <summary>
    /// Type of mention.
    /// </summary>
    public MentionType Type { get; init; }

    /// <summary>
    /// Grammatical gender (if applicable).
    /// </summary>
    public GrammaticalGender Gender { get; init; } = GrammaticalGender.Unknown;

    /// <summary>
    /// Grammatical number (singular/plural).
    /// </summary>
    public GrammaticalNumber Number { get; init; } = GrammaticalNumber.Unknown;

    /// <summary>
    /// Whether this is the first mention of the entity.
    /// </summary>
    public bool IsFirstMention { get; init; }
}

/// <summary>
/// An unresolved mention that couldn't be linked to an entity.
/// </summary>
public sealed class UnresolvedMention
{
    /// <summary>
    /// The unresolved mention.
    /// </summary>
    public required EntityMention Mention { get; init; }

    /// <summary>
    /// Reason why resolution failed.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Candidate entities that were considered.
    /// </summary>
    public List<Entity> CandidateEntities { get; init; } = [];
}

/// <summary>
/// Types of coreference.
/// </summary>
public enum CoreferenceType
{
    /// <summary>
    /// Personal pronoun (he, she, it, they).
    /// </summary>
    PersonalPronoun,

    /// <summary>
    /// Possessive pronoun (his, her, its, their).
    /// </summary>
    PossessivePronoun,

    /// <summary>
    /// Reflexive pronoun (himself, herself, itself).
    /// </summary>
    ReflexivePronoun,

    /// <summary>
    /// Demonstrative pronoun (this, that, these, those).
    /// </summary>
    DemonstrativePronoun,

    /// <summary>
    /// Definite description (the CEO, the company).
    /// </summary>
    DefiniteDescription,

    /// <summary>
    /// Abbreviated reference (the org, the proj).
    /// </summary>
    AbbreviatedReference,

    /// <summary>
    /// Name variation (John → Mr. Smith).
    /// </summary>
    NameVariation,

    /// <summary>
    /// Generic reference (one, someone, anyone).
    /// </summary>
    GenericReference
}

/// <summary>
/// Types of entity mentions.
/// </summary>
public enum MentionType
{
    /// <summary>
    /// Proper name (John, Microsoft).
    /// </summary>
    ProperName,

    /// <summary>
    /// Common noun phrase (the company, the meeting).
    /// </summary>
    NounPhrase,

    /// <summary>
    /// Pronoun (he, she, it).
    /// </summary>
    Pronoun,

    /// <summary>
    /// Possessive (his, her, its).
    /// </summary>
    Possessive,

    /// <summary>
    /// Demonstrative (this, that).
    /// </summary>
    Demonstrative
}

/// <summary>
/// Grammatical gender.
/// </summary>
public enum GrammaticalGender
{
    Unknown,
    Masculine,
    Feminine,
    Neuter,
    Animate  // For "they" referring to people
}

/// <summary>
/// Grammatical number.
/// </summary>
public enum GrammaticalNumber
{
    Unknown,
    Singular,
    Plural
}

/// <summary>
/// Options for text expansion.
/// </summary>
public sealed class ExpansionOptions
{
    /// <summary>
    /// Whether to include both the pronoun and entity name.
    /// Example: "he" → "John (he)" vs "he" → "John".
    /// Default: false.
    /// </summary>
    public bool IncludeOriginalPronoun { get; init; }

    /// <summary>
    /// Minimum confidence score for replacement.
    /// Default: 0.7.
    /// </summary>
    public float MinConfidence { get; init; } = 0.7f;

    /// <summary>
    /// Whether to replace only ambiguous pronouns.
    /// Default: false.
    /// </summary>
    public bool OnlyAmbiguous { get; init; }

    /// <summary>
    /// Format for the replacement.
    /// {0} = entity name, {1} = original pronoun.
    /// Default: "{0}".
    /// </summary>
    public string ReplacementFormat { get; init; } = "{0}";
}
