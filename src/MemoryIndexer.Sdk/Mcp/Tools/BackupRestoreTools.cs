using System.ComponentModel;
using System.Text.Json;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using MemoryIndexer.Configuration;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for memory backup and restore operations.
/// Phase v0.6.0-β: Memory Export/Import (Backup/Restore).
/// </summary>
[McpServerToolType]
public class BackupRestoreTools(IMemoryExporter exporter, IOptions<MemoryIndexerOptions> indexerOptions)
{
    private static readonly JsonSerializerOptions s_indentedJsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions s_caseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Export memories to JSON format for backup.
    /// </summary>
    [McpServerTool]
    [Description("Export memories to JSON format for backup. Returns the exported data as a JSON string that can be saved to a file.")]
    public async Task<ExportResult> ExportMemories(
        [Description("Include embeddings in export (can be large)")] bool includeEmbeddings = false,
        [Description("Only export memories created/updated after this ISO 8601 timestamp")] string? since = null,
        [Description("Memory tiers to include: buffer, short, long, archive (comma-separated)")] string? tiers = null,
        [Description("Memory types to include: episodic, semantic, procedural, fact, reflection (comma-separated)")] string? types = null,
        [Description("Session ID to export (null for all sessions)")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var options = new ExportOptions
        {
            UserId = indexerOptions.Value.DefaultUserId,
            IncludeEmbeddings = includeEmbeddings,
            IncludeMetadata = true,
            SessionId = sessionId
        };

        if (!string.IsNullOrWhiteSpace(since) && DateTimeOffset.TryParse(since, out var sinceDate))
        {
            options.Since = sinceDate;
        }

        if (!string.IsNullOrWhiteSpace(tiers))
        {
            options.Tiers = ParseTiers(tiers);
        }

        if (!string.IsNullOrWhiteSpace(types))
        {
            options.Types = ParseTypes(types);
        }

        var package = await exporter.ExportAsync(options, cancellationToken);

        return new ExportResult
        {
            Success = true,
            TotalMemories = package.Statistics.TotalMemories,
            ByTier = package.Statistics.ByTier.ToDictionary(k => k.Key.ToString(), v => v.Value),
            ByType = package.Statistics.ByType.ToDictionary(k => k.Key.ToString(), v => v.Value),
            ExportedAt = package.ExportedAt.ToString("O"),
            Checksum = package.Checksum,
            JsonData = JsonSerializer.Serialize(package, s_indentedJsonOptions),
            Message = $"Successfully exported {package.Statistics.TotalMemories} memories"
        };
    }

    /// <summary>
    /// Import memories from a JSON backup.
    /// </summary>
    [McpServerTool]
    [Description("Import memories from a JSON backup. Provide the JSON data that was previously exported.")]
    public async Task<ImportResultSummary> ImportMemories(
        [Description("The JSON data from a previous export")] string jsonData,
        [Description("How to handle conflicts: skip, replace, keepNewer, keepHigherConfidence, fail")] string conflictResolution = "skip",
        [Description("Whether to regenerate embeddings (use if migrating between embedding providers)")] bool regenerateEmbeddings = false,
        [Description("Preserve original timestamps (false to use current time)")] bool preserveTimestamps = true,
        [Description("Preserve original IDs (false to generate new IDs)")] bool preserveIds = true,
        CancellationToken cancellationToken = default)
    {
        MemoryExportPackage? package;
        try
        {
            package = JsonSerializer.Deserialize<MemoryExportPackage>(jsonData, s_caseInsensitiveJsonOptions);
        }
        catch (JsonException ex)
        {
            return new ImportResultSummary
            {
                Success = false,
                Message = $"Failed to parse JSON data: {ex.Message}"
            };
        }

        if (package == null)
        {
            return new ImportResultSummary
            {
                Success = false,
                Message = "Invalid export package: deserialization returned null"
            };
        }

        var options = new ImportOptions
        {
            ConflictResolution = ParseImportConflictResolution(conflictResolution),
            RegenerateEmbeddings = regenerateEmbeddings,
            PreserveTimestamps = preserveTimestamps,
            PreserveIds = preserveIds,
            ValidateBeforeImport = true
        };

        var result = await exporter.ImportAsync(package, options, cancellationToken);

        return new ImportResultSummary
        {
            Success = result.Success,
            ImportedCount = result.ImportedCount,
            SkippedCount = result.SkippedCount,
            ReplacedCount = result.ReplacedCount,
            FailedCount = result.FailedCount,
            ConflictsCount = result.Conflicts.Count,
            ErrorsCount = result.Errors.Count,
            DurationMs = (int)result.Duration.TotalMilliseconds,
            Message = result.Success
                ? $"Import completed: {result.ImportedCount} imported, {result.SkippedCount} skipped"
                : $"Import had issues: {result.FailedCount} failed, {result.Errors.Count} errors"
        };
    }

    /// <summary>
    /// Get backup statistics without exporting data.
    /// </summary>
    [McpServerTool]
    [Description("Get statistics about memories that would be exported, without actually exporting the data.")]
    public async Task<BackupStatsResult> GetBackupStats(
        [Description("Only count memories created/updated after this ISO 8601 timestamp")] string? since = null,
        [Description("Memory tiers to include: buffer, short, long, archive (comma-separated)")] string? tiers = null,
        [Description("Session ID to analyze (null for all sessions)")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var options = new ExportOptions
        {
            UserId = indexerOptions.Value.DefaultUserId,
            IncludeEmbeddings = false, // Don't need embeddings for stats
            IncludeMetadata = false,
            SessionId = sessionId
        };

        if (!string.IsNullOrWhiteSpace(since) && DateTimeOffset.TryParse(since, out var sinceDate))
        {
            options.Since = sinceDate;
        }

        if (!string.IsNullOrWhiteSpace(tiers))
        {
            options.Tiers = ParseTiers(tiers);
        }

        var package = await exporter.ExportAsync(options, cancellationToken);

        return new BackupStatsResult
        {
            Success = true,
            TotalMemories = package.Statistics.TotalMemories,
            ByTier = package.Statistics.ByTier.ToDictionary(k => k.Key.ToString(), v => v.Value),
            ByType = package.Statistics.ByType.ToDictionary(k => k.Key.ToString(), v => v.Value),
            UniqueUsers = package.Statistics.UniqueUsers,
            UniqueSessions = package.Statistics.UniqueSessions,
            EarliestMemory = package.Statistics.EarliestMemory?.ToString("O"),
            LatestMemory = package.Statistics.LatestMemory?.ToString("O"),
            Message = $"Found {package.Statistics.TotalMemories} memories ready for backup"
        };
    }

    private static List<Tier> ParseTiers(string tiers)
    {
        var result = new List<Tier>();
        foreach (var tier in tiers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<Tier>(tier, ignoreCase: true, out var parsed))
            {
                result.Add(parsed);
            }
        }
        return result;
    }

    private static List<MemoryType> ParseTypes(string types)
    {
        var result = new List<MemoryType>();
        foreach (var type in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<MemoryType>(type, ignoreCase: true, out var parsed))
            {
                result.Add(parsed);
            }
        }
        return result;
    }

    private static ImportConflictResolution ParseImportConflictResolution(string resolution)
    {
        return resolution.ToLowerInvariant() switch
        {
            "skip" => ImportConflictResolution.Skip,
            "replace" => ImportConflictResolution.Replace,
            "keepnewer" => ImportConflictResolution.KeepNewer,
            "keephigherconfidence" => ImportConflictResolution.KeepHigherConfidence,
            "fail" => ImportConflictResolution.Fail,
            _ => ImportConflictResolution.Skip
        };
    }
}

/// <summary>
/// Result of an export operation via MCP.
/// </summary>
public class ExportResult
{
    public bool Success { get; set; }
    public int TotalMemories { get; set; }
    public Dictionary<string, int>? ByTier { get; set; }
    public Dictionary<string, int>? ByType { get; set; }
    public string? ExportedAt { get; set; }
    public string? Checksum { get; set; }
    public string? JsonData { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Summary of an import operation via MCP.
/// </summary>
public class ImportResultSummary
{
    public bool Success { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ReplacedCount { get; set; }
    public int FailedCount { get; set; }
    public int ConflictsCount { get; set; }
    public int ErrorsCount { get; set; }
    public int DurationMs { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Backup statistics result via MCP.
/// </summary>
public class BackupStatsResult
{
    public bool Success { get; set; }
    public int TotalMemories { get; set; }
    public Dictionary<string, int>? ByTier { get; set; }
    public Dictionary<string, int>? ByType { get; set; }
    public int UniqueUsers { get; set; }
    public int UniqueSessions { get; set; }
    public string? EarliestMemory { get; set; }
    public string? LatestMemory { get; set; }
    public string? Message { get; set; }
}
