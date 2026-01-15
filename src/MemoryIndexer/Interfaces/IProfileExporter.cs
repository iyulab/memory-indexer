namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for exporting user profile data.
/// Phase v0.10.0: User Profile Evolution - GDPR Compliance.
/// </summary>
public interface IProfileExporter
{
    /// <summary>
    /// Format supported by this exporter.
    /// </summary>
    string Format { get; }

    /// <summary>
    /// Exports a user's complete profile data.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="options">Export options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Export result with data.</returns>
    Task<ProfileExportResult> ExportAsync(
        string userId,
        ProfileExportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a user's profile to a stream.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="outputStream">Stream to write to.</param>
    /// <param name="options">Export options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Export metadata.</returns>
    Task<ProfileExportMetadata> ExportToStreamAsync(
        string userId,
        Stream outputStream,
        ProfileExportOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for profile export.
/// </summary>
public class ProfileExportOptions
{
    /// <summary>
    /// Include archived (inactive) facts.
    /// Default: true
    /// </summary>
    public bool IncludeArchived { get; set; } = true;

    /// <summary>
    /// Include version history for facts.
    /// Default: true
    /// </summary>
    public bool IncludeHistory { get; set; } = true;

    /// <summary>
    /// Include inferred facts.
    /// Default: false
    /// </summary>
    public bool IncludeInferred { get; set; } = false;

    /// <summary>
    /// Include metadata (embeddings, etc.).
    /// Default: false (embeddings can be large)
    /// </summary>
    public bool IncludeMetadata { get; set; } = false;

    /// <summary>
    /// Include audit trail (access history).
    /// Default: false
    /// </summary>
    public bool IncludeAuditTrail { get; set; } = false;

    /// <summary>
    /// Categories to include.
    /// Default: All categories.
    /// </summary>
    public IReadOnlyList<SemanticStoreCategory>? IncludeCategories { get; set; }

    /// <summary>
    /// Categories to exclude.
    /// Default: None.
    /// </summary>
    public IReadOnlyList<SemanticStoreCategory>? ExcludeCategories { get; set; }

    /// <summary>
    /// Date range start (only facts updated after this).
    /// </summary>
    public DateTime? Since { get; set; }

    /// <summary>
    /// Date range end (only facts updated before this).
    /// </summary>
    public DateTime? Until { get; set; }

    /// <summary>
    /// Whether to redact sensitive information.
    /// Default: false
    /// </summary>
    public bool RedactSensitive { get; set; } = false;

    /// <summary>
    /// Patterns to redact (if RedactSensitive is true).
    /// </summary>
    public IReadOnlyList<string>? RedactionPatterns { get; set; }
}

/// <summary>
/// Result of profile export.
/// </summary>
public class ProfileExportResult
{
    /// <summary>
    /// Whether export was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Export metadata.
    /// </summary>
    public ProfileExportMetadata? Metadata { get; set; }

    /// <summary>
    /// Exported data as string (for text formats like JSON).
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// Exported data as bytes (for binary formats).
    /// </summary>
    public byte[]? RawData { get; set; }
}

/// <summary>
/// Metadata about an export operation.
/// </summary>
public class ProfileExportMetadata
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Export format.
    /// </summary>
    public required string Format { get; set; }

    /// <summary>
    /// When the export was created.
    /// </summary>
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Memory Indexer version.
    /// </summary>
    public string Version { get; set; } = "0.10.0";

    /// <summary>
    /// Total facts exported.
    /// </summary>
    public int FactCount { get; set; }

    /// <summary>
    /// Active facts count.
    /// </summary>
    public int ActiveFactCount { get; set; }

    /// <summary>
    /// Archived facts count.
    /// </summary>
    public int ArchivedFactCount { get; set; }

    /// <summary>
    /// Facts by category.
    /// </summary>
    public IReadOnlyDictionary<string, int> FactsByCategory { get; set; } =
        new Dictionary<string, int>();

    /// <summary>
    /// Earliest fact date.
    /// </summary>
    public DateTime? EarliestFact { get; set; }

    /// <summary>
    /// Latest fact date.
    /// </summary>
    public DateTime? LatestFact { get; set; }

    /// <summary>
    /// Export size in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Checksum for data integrity.
    /// </summary>
    public string? Checksum { get; set; }

    /// <summary>
    /// Options used for this export.
    /// </summary>
    public ProfileExportOptions? Options { get; set; }
}

/// <summary>
/// Exported profile data structure (for JSON export).
/// </summary>
public class ExportedProfile
{
    /// <summary>
    /// Export metadata.
    /// </summary>
    public required ProfileExportMetadata Metadata { get; set; }

    /// <summary>
    /// Active facts.
    /// </summary>
    public IReadOnlyList<ExportedFact> ActiveFacts { get; set; } = [];

    /// <summary>
    /// Archived facts (if included).
    /// </summary>
    public IReadOnlyList<ExportedFact>? ArchivedFacts { get; set; }

    /// <summary>
    /// Inferred facts (if included).
    /// </summary>
    public IReadOnlyList<ExportedFact>? InferredFacts { get; set; }
}

/// <summary>
/// A single fact in exported format.
/// </summary>
public class ExportedFact
{
    /// <summary>
    /// Fact key.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Fact value.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Category.
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Confidence (0-1).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Number of confirmations.
    /// </summary>
    public int ConfirmationCount { get; set; }

    /// <summary>
    /// When fact was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When fact was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When fact became valid (bi-temporal).
    /// </summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>
    /// When fact stopped being valid (bi-temporal).
    /// </summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>
    /// Whether fact is currently active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Key of fact this supersedes.
    /// </summary>
    public string? SupersedesKey { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Source sessions where this fact was mentioned.
    /// </summary>
    public IReadOnlyList<string>? SourceSessions { get; set; }
}

/// <summary>
/// Sensitivity classification for facts.
/// </summary>
public enum FactSensitivity
{
    /// <summary>General, non-sensitive information.</summary>
    Public,

    /// <summary>Personal but not sensitive (e.g., preferences).</summary>
    Personal,

    /// <summary>Sensitive personal information (e.g., health).</summary>
    Sensitive,

    /// <summary>Highly sensitive (e.g., financial, medical details).</summary>
    HighlySensitive
}

/// <summary>
/// Mapping of categories to sensitivity levels.
/// </summary>
public static class FactSensitivityMapping
{
    /// <summary>
    /// Default sensitivity by category.
    /// </summary>
    public static readonly IReadOnlyDictionary<FactCategory, FactSensitivity> ByCategoryDefault =
        new Dictionary<FactCategory, FactSensitivity>
        {
            [FactCategory.Identity] = FactSensitivity.Sensitive,
            [FactCategory.Preference] = FactSensitivity.Personal,
            [FactCategory.Relationship] = FactSensitivity.Personal,
            [FactCategory.Location] = FactSensitivity.Sensitive,
            [FactCategory.Temporal] = FactSensitivity.Personal,
            [FactCategory.Skill] = FactSensitivity.Public,
            [FactCategory.Goal] = FactSensitivity.Personal,
            [FactCategory.Health] = FactSensitivity.HighlySensitive,
            [FactCategory.Professional] = FactSensitivity.Personal,
            [FactCategory.General] = FactSensitivity.Public
        };
}
