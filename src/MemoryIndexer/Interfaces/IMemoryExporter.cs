using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Interface for memory export and import operations.
/// Supports backup/restore workflows with incremental and full export capabilities.
/// Phase v0.6.0-β: Memory Export/Import (Backup/Restore).
/// </summary>
public interface IMemoryExporter
{
    /// <summary>
    /// Exports memories to a serialized format.
    /// </summary>
    /// <param name="options">Export configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Export package containing serialized memories and metadata.</returns>
    Task<MemoryExportPackage> ExportAsync(
        ExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports memories from a serialized package.
    /// </summary>
    /// <param name="package">The export package to import.</param>
    /// <param name="options">Import configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with statistics and any conflicts.</returns>
    Task<ImportResult> ImportAsync(
        MemoryExportPackage package,
        ImportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports memories to a stream in the specified format.
    /// </summary>
    /// <param name="stream">The output stream to write to.</param>
    /// <param name="options">Export configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Export statistics.</returns>
    Task<ExportStatistics> ExportToStreamAsync(
        Stream stream,
        ExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports memories from a stream.
    /// </summary>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="options">Import configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with statistics and any conflicts.</returns>
    Task<ImportResult> ImportFromStreamAsync(
        Stream stream,
        ImportOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Export configuration options.
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// User ID to export memories for. If null, exports all users.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Session ID to export. If null, exports all sessions.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Export only memories created or updated after this timestamp.
    /// Used for incremental backups.
    /// </summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>
    /// Export only memories created or updated before this timestamp.
    /// </summary>
    public DateTimeOffset? Until { get; set; }

    /// <summary>
    /// Memory tiers to include in export. If null or empty, exports all tiers.
    /// </summary>
    public IReadOnlyList<Tier>? Tiers { get; set; }

    /// <summary>
    /// Memory types to include in export. If null or empty, exports all types.
    /// </summary>
    public IReadOnlyList<MemoryType>? Types { get; set; }

    /// <summary>
    /// Whether to include embeddings in the export.
    /// Embeddings can be large; set to false for lightweight exports.
    /// </summary>
    public bool IncludeEmbeddings { get; set; } = true;

    /// <summary>
    /// Whether to include metadata in the export.
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// Whether to include soft-deleted memories.
    /// </summary>
    public bool IncludeDeleted { get; set; } = false;

    /// <summary>
    /// Export format version for compatibility.
    /// </summary>
    public string FormatVersion { get; set; } = "1.0";
}

/// <summary>
/// Import configuration options.
/// </summary>
public class ImportOptions
{
    /// <summary>
    /// How to handle conflicts when a memory with the same ID already exists.
    /// </summary>
    public ImportConflictResolution ConflictResolution { get; set; } = ImportConflictResolution.Skip;

    /// <summary>
    /// Whether to regenerate embeddings on import.
    /// Useful when migrating between embedding providers.
    /// </summary>
    public bool RegenerateEmbeddings { get; set; } = false;

    /// <summary>
    /// Whether to preserve original timestamps.
    /// If false, imported memories get new timestamps.
    /// </summary>
    public bool PreserveTimestamps { get; set; } = true;

    /// <summary>
    /// Whether to preserve original IDs.
    /// If false, imported memories get new IDs.
    /// </summary>
    public bool PreserveIds { get; set; } = true;

    /// <summary>
    /// Override user ID for all imported memories.
    /// If null, uses the original user ID from the export.
    /// </summary>
    public string? OverrideUserId { get; set; }

    /// <summary>
    /// Override session ID for all imported memories.
    /// If null, uses the original session ID from the export.
    /// </summary>
    public string? OverrideSessionId { get; set; }

    /// <summary>
    /// Maximum number of memories to import in a single batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Whether to validate memories before importing.
    /// </summary>
    public bool ValidateBeforeImport { get; set; } = true;
}

/// <summary>
/// Conflict resolution strategy for import operations.
/// </summary>
public enum ImportConflictResolution
{
    /// <summary>
    /// Skip existing memories, import only new ones.
    /// </summary>
    Skip,

    /// <summary>
    /// Replace existing memories with imported ones.
    /// </summary>
    Replace,

    /// <summary>
    /// Keep the memory with the most recent update timestamp.
    /// </summary>
    KeepNewer,

    /// <summary>
    /// Keep the memory with the higher confidence score.
    /// </summary>
    KeepHigherConfidence,

    /// <summary>
    /// Fail the import if any conflicts are detected.
    /// </summary>
    Fail
}

/// <summary>
/// Export package containing serialized memories and metadata.
/// </summary>
public class MemoryExportPackage
{
    /// <summary>
    /// Format version for compatibility checking.
    /// </summary>
    public string FormatVersion { get; set; } = "1.0";

    /// <summary>
    /// Export timestamp.
    /// </summary>
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Source system identifier.
    /// </summary>
    public string? SourceSystem { get; set; }

    /// <summary>
    /// Export options used to create this package.
    /// </summary>
    public ExportOptions? Options { get; set; }

    /// <summary>
    /// The exported memories.
    /// </summary>
    public IReadOnlyList<MemoryUnit> Memories { get; set; } = [];

    /// <summary>
    /// Export statistics.
    /// </summary>
    public ExportStatistics Statistics { get; set; } = new();

    /// <summary>
    /// Optional checksum for data integrity verification.
    /// </summary>
    public string? Checksum { get; set; }
}

/// <summary>
/// Statistics about an export operation.
/// </summary>
public class ExportStatistics
{
    /// <summary>
    /// Total number of memories exported.
    /// </summary>
    public int TotalMemories { get; set; }

    /// <summary>
    /// Number of memories by tier.
    /// </summary>
    public Dictionary<Tier, int> ByTier { get; set; } = [];

    /// <summary>
    /// Number of memories by type.
    /// </summary>
    public Dictionary<MemoryType, int> ByType { get; set; } = [];

    /// <summary>
    /// Number of unique users in the export.
    /// </summary>
    public int UniqueUsers { get; set; }

    /// <summary>
    /// Number of unique sessions in the export.
    /// </summary>
    public int UniqueSessions { get; set; }

    /// <summary>
    /// Earliest memory timestamp in the export.
    /// </summary>
    public DateTimeOffset? EarliestMemory { get; set; }

    /// <summary>
    /// Latest memory timestamp in the export.
    /// </summary>
    public DateTimeOffset? LatestMemory { get; set; }

    /// <summary>
    /// Total size in bytes of the export.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Duration of the export operation.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of an import operation.
/// </summary>
public class ImportResult
{
    /// <summary>
    /// Whether the import was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of memories successfully imported.
    /// </summary>
    public int ImportedCount { get; set; }

    /// <summary>
    /// Number of memories skipped due to conflicts.
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Number of memories that replaced existing ones.
    /// </summary>
    public int ReplacedCount { get; set; }

    /// <summary>
    /// Number of memories that failed to import.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Details of any conflicts encountered.
    /// </summary>
    public IReadOnlyList<ImportConflict> Conflicts { get; set; } = [];

    /// <summary>
    /// Details of any errors encountered.
    /// </summary>
    public IReadOnlyList<ImportError> Errors { get; set; } = [];

    /// <summary>
    /// Duration of the import operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Mapping of original IDs to new IDs (when PreserveIds is false).
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid>? IdMapping { get; set; }
}

/// <summary>
/// Details about an import conflict.
/// </summary>
public class ImportConflict
{
    /// <summary>
    /// ID of the conflicting memory.
    /// </summary>
    public Guid MemoryId { get; set; }

    /// <summary>
    /// Content preview of the imported memory.
    /// </summary>
    public string? ImportedContent { get; set; }

    /// <summary>
    /// Content preview of the existing memory.
    /// </summary>
    public string? ExistingContent { get; set; }

    /// <summary>
    /// How the conflict was resolved.
    /// </summary>
    public ImportConflictResolution Resolution { get; set; }

    /// <summary>
    /// Which memory was kept (Imported or Existing).
    /// </summary>
    public string? Outcome { get; set; }
}

/// <summary>
/// Details about an import error.
/// </summary>
public class ImportError
{
    /// <summary>
    /// ID of the memory that failed to import.
    /// </summary>
    public Guid? MemoryId { get; set; }

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Index in the import batch where the error occurred.
    /// </summary>
    public int? Index { get; set; }
}
