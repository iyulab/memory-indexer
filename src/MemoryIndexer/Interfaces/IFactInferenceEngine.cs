namespace MemoryIndexer.Interfaces;

/// <summary>
/// Engine for deriving new facts from existing facts through inference.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
/// <remarks>
/// Supports multiple inference types:
/// - Transitive: A→B, B→C ⟹ A→C
/// - Co-occurrence: A ∧ B ⟹ C
/// - Semantic: Similar facts imply relationships
/// </remarks>
public interface IFactInferenceEngine
{
    /// <summary>
    /// Infers new facts from a set of existing facts.
    /// </summary>
    /// <param name="facts">Existing facts to analyze.</param>
    /// <param name="options">Inference options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of inferred facts with evidence.</returns>
    Task<IReadOnlyList<InferredFact>> InferAsync(
        IReadOnlyList<SemanticStoreEntry> facts,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs inference on a user's complete profile.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="options">Inference options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Inference result with all derived facts.</returns>
    Task<InferenceResult> InferForUserAsync(
        string userId,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a custom inference rule.
    /// </summary>
    /// <param name="rule">The rule to register.</param>
    void RegisterRule(IInferenceRule rule);

    /// <summary>
    /// Gets all registered inference rules.
    /// </summary>
    /// <returns>List of registered rules.</returns>
    IReadOnlyList<IInferenceRule> GetRegisteredRules();
}

/// <summary>
/// A fact derived through inference.
/// </summary>
public class InferredFact
{
    /// <summary>
    /// The inferred fact content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Category of the inferred fact.
    /// </summary>
    public FactCategory Category { get; set; } = FactCategory.General;

    /// <summary>
    /// Confidence in this inference (0-1).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Type of inference that produced this fact.
    /// </summary>
    public InferenceType Type { get; set; }

    /// <summary>
    /// The rule that produced this inference.
    /// </summary>
    public string? RuleName { get; set; }

    /// <summary>
    /// Source facts that contributed to this inference.
    /// </summary>
    public IReadOnlyList<string> SourceFactKeys { get; set; } = [];

    /// <summary>
    /// Human-readable explanation of how this was inferred.
    /// </summary>
    public string? Explanation { get; set; }

    /// <summary>
    /// Whether this inference should be stored as a new fact.
    /// </summary>
    public bool ShouldStore { get; set; }

    /// <summary>
    /// Subject-Predicate-Object representation if extractable.
    /// </summary>
    public InferredTriple? Triple { get; set; }
}

/// <summary>
/// SPO triple for an inferred fact.
/// </summary>
public class InferredTriple
{
    /// <summary>Subject of the triple.</summary>
    public required string Subject { get; set; }

    /// <summary>Predicate (relationship) of the triple.</summary>
    public required string Predicate { get; set; }

    /// <summary>Object (target) of the triple.</summary>
    public required string Object { get; set; }
}

/// <summary>
/// Types of inference.
/// </summary>
public enum InferenceType
{
    /// <summary>
    /// Transitive inference: A→B, B→C ⟹ A→C.
    /// Example: "lives in Seoul" + "Seoul is in Korea" ⟹ "lives in Korea"
    /// </summary>
    Transitive,

    /// <summary>
    /// Co-occurrence inference: A ∧ B ⟹ C.
    /// Example: "works at Samsung" + "lives in Seoul" ⟹ "commutes in Seoul"
    /// </summary>
    CoOccurrence,

    /// <summary>
    /// Semantic inference based on embedding similarity.
    /// Example: Similar facts imply related preferences.
    /// </summary>
    Semantic,

    /// <summary>
    /// Temporal inference based on time patterns.
    /// Example: Regular morning coffee → "prefers morning coffee routine"
    /// </summary>
    Temporal,

    /// <summary>
    /// Negation inference from contradicting patterns.
    /// Example: "dislikes X" ⟹ "does not prefer X"
    /// </summary>
    Negation,

    /// <summary>
    /// Generalization from specific instances.
    /// Example: Multiple tech purchases → "interested in technology"
    /// </summary>
    Generalization,

    /// <summary>
    /// Custom rule-based inference.
    /// </summary>
    Custom
}

/// <summary>
/// Options for controlling inference behavior.
/// </summary>
public class InferenceOptions
{
    /// <summary>
    /// Minimum confidence for source facts to be considered.
    /// Default: 0.7
    /// </summary>
    public float MinSourceConfidence { get; set; } = 0.7f;

    /// <summary>
    /// Minimum confidence for inferred facts to be returned.
    /// Default: 0.5
    /// </summary>
    public float MinInferenceConfidence { get; set; } = 0.5f;

    /// <summary>
    /// Maximum inference depth (chain length).
    /// Default: 2
    /// </summary>
    public int MaxDepth { get; set; } = 2;

    /// <summary>
    /// Types of inference to run.
    /// Default: All types.
    /// </summary>
    public IReadOnlyList<InferenceType>? EnabledTypes { get; set; }

    /// <summary>
    /// Categories to include in inference.
    /// Default: All categories.
    /// </summary>
    public IReadOnlyList<FactCategory>? IncludeCategories { get; set; }

    /// <summary>
    /// Maximum number of inferred facts to return.
    /// Default: 50
    /// </summary>
    public int MaxResults { get; set; } = 50;

    /// <summary>
    /// Whether to automatically store high-confidence inferences.
    /// Default: false
    /// </summary>
    public bool AutoStore { get; set; } = false;

    /// <summary>
    /// Confidence threshold for auto-store.
    /// Default: 0.85
    /// </summary>
    public float AutoStoreThreshold { get; set; } = 0.85f;
}

/// <summary>
/// Result of inference operation.
/// </summary>
public class InferenceResult
{
    /// <summary>
    /// User ID for whom inference was performed.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Inferred facts.
    /// </summary>
    public IReadOnlyList<InferredFact> InferredFacts { get; set; } = [];

    /// <summary>
    /// Number of source facts analyzed.
    /// </summary>
    public int SourceFactCount { get; set; }

    /// <summary>
    /// Number of inferences stored (if auto-store enabled).
    /// </summary>
    public int StoredCount { get; set; }

    /// <summary>
    /// Statistics by inference type.
    /// </summary>
    public IReadOnlyDictionary<InferenceType, int> ByType { get; set; } =
        new Dictionary<InferenceType, int>();

    /// <summary>
    /// Statistics by category.
    /// </summary>
    public IReadOnlyDictionary<FactCategory, int> ByCategory { get; set; } =
        new Dictionary<FactCategory, int>();

    /// <summary>
    /// Processing time in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}

/// <summary>
/// Interface for custom inference rules.
/// </summary>
public interface IInferenceRule
{
    /// <summary>
    /// Unique name of the rule.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this rule infers.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Type of inference this rule performs.
    /// </summary>
    InferenceType Type { get; }

    /// <summary>
    /// Evaluates facts and returns inferred facts.
    /// </summary>
    /// <param name="facts">Source facts to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Inferred facts from this rule.</returns>
    Task<IReadOnlyList<InferredFact>> EvaluateAsync(
        IReadOnlyList<SemanticStoreEntry> facts,
        CancellationToken cancellationToken = default);
}
