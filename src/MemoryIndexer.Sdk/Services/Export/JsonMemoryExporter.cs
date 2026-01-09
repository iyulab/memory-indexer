using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Observability;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Services.Export;

/// <summary>
/// JSON-based memory exporter for backup/restore operations.
/// Phase v0.6.0-β: Memory Export/Import (Backup/Restore).
/// </summary>
public class JsonMemoryExporter : IMemoryExporter
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<JsonMemoryExporter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public JsonMemoryExporter(
        IMemoryStore memoryStore,
        ILogger<JsonMemoryExporter> logger,
        IEmbeddingService? embeddingService = null)
    {
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddingService = embeddingService;
    }

    /// <inheritdoc />
    public async Task<MemoryExportPackage> ExportAsync(
        ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("ExportMemories", "export");
        activity?.SetTag("export.format", "json");
        activity?.SetTag("export.user_id", options.UserId ?? "all");
        activity?.SetTag("export.include_embeddings", options.IncludeEmbeddings);

        var sw = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting memory export for user: {UserId}", options.UserId ?? "all");

            // Fetch memories based on options
            var memories = await FetchMemoriesAsync(options, cancellationToken);
            
            // Filter by options
            var filteredMemories = FilterMemories(memories, options);

            // Prepare export package
            var package = new MemoryExportPackage
            {
                FormatVersion = options.FormatVersion,
                ExportedAt = DateTimeOffset.UtcNow,
                SourceSystem = "MemoryIndexer",
                Options = options,
                Memories = PrepareMemoriesForExport(filteredMemories, options),
                Statistics = CalculateStatistics(filteredMemories)
            };

            sw.Stop();
            package.Statistics.Duration = sw.Elapsed;

            // Calculate checksum for integrity verification
            package.Checksum = CalculateChecksum(package);

            activity?.SetTag("export.memory_count", package.Statistics.TotalMemories);
            activity?.SetTag("export.unique_users", package.Statistics.UniqueUsers);
            MemoryIndexerTelemetry.CompleteOperation(activity, success: true);

            _logger.LogInformation(
                "Export completed: {Count} memories in {Duration}ms",
                package.Statistics.TotalMemories,
                sw.ElapsedMilliseconds);

            return package;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed");
            MemoryIndexerTelemetry.CompleteOperation(activity, success: false, exception: ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(
        MemoryExportPackage package,
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("ImportMemories", "import");
        activity?.SetTag("import.format", "json");
        activity?.SetTag("import.conflict_resolution", options.ConflictResolution.ToString());
        activity?.SetTag("import.memory_count", package.Memories.Count);

        var sw = Stopwatch.StartNew();
        var result = new ImportResult { Success = true };
        var conflicts = new List<ImportConflict>();
        var errors = new List<ImportError>();
        var idMapping = new Dictionary<Guid, Guid>();

        try
        {
            _logger.LogInformation(
                "Starting memory import: {Count} memories, conflict resolution: {Resolution}",
                package.Memories.Count,
                options.ConflictResolution);

            // Verify checksum if present
            if (!string.IsNullOrEmpty(package.Checksum))
            {
                var packageForChecksum = ClonePackageWithoutChecksum(package);
                var calculatedChecksum = CalculateChecksum(packageForChecksum);
                if (calculatedChecksum != package.Checksum)
                {
                    _logger.LogWarning("Package checksum mismatch - data may be corrupted");
                }
            }

            // Validate memories if requested
            if (options.ValidateBeforeImport)
            {
                ValidatePackage(package, errors);
                if (errors.Count > 0 && options.ConflictResolution == ImportConflictResolution.Fail)
                {
                    result.Success = false;
                    result.Errors = errors;
                    return result;
                }
            }

            // Process in batches
            var batches = package.Memories
                .Select((m, i) => (Memory: m, Index: i))
                .Chunk(options.BatchSize);

            foreach (var batch in batches)
            {
                await ProcessImportBatchAsync(
                    batch,
                    options,
                    conflicts,
                    errors,
                    idMapping,
                    result,
                    cancellationToken);
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Conflicts = conflicts;
            result.Errors = errors;
            if (!options.PreserveIds && idMapping.Count > 0)
            {
                result.IdMapping = idMapping;
            }

            activity?.SetTag("import.imported_count", result.ImportedCount);
            activity?.SetTag("import.skipped_count", result.SkippedCount);
            activity?.SetTag("import.failed_count", result.FailedCount);
            MemoryIndexerTelemetry.CompleteOperation(activity, success: result.Success);

            _logger.LogInformation(
                "Import completed: {Imported} imported, {Skipped} skipped, {Failed} failed in {Duration}ms",
                result.ImportedCount,
                result.SkippedCount,
                result.FailedCount,
                sw.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed");
            result.Success = false;
            result.Duration = sw.Elapsed;
            result.Conflicts = conflicts;
            result.Errors = errors;
            errors.Add(new ImportError { Message = ex.Message });
            MemoryIndexerTelemetry.CompleteOperation(activity, success: false, exception: ex);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<ExportStatistics> ExportToStreamAsync(
        Stream stream,
        ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var package = await ExportAsync(options, cancellationToken);
        
        await JsonSerializer.SerializeAsync(stream, package, JsonOptions, cancellationToken);
        
        package.Statistics.SizeBytes = stream.Position;
        return package.Statistics;
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromStreamAsync(
        Stream stream,
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var package = await JsonSerializer.DeserializeAsync<MemoryExportPackage>(
            stream, 
            JsonOptions, 
            cancellationToken);

        if (package == null)
        {
            return new ImportResult
            {
                Success = false,
                Errors = [new ImportError { Message = "Failed to deserialize export package" }]
            };
        }

        return await ImportAsync(package, options, cancellationToken);
    }

    private async Task<IReadOnlyList<MemoryUnit>> FetchMemoriesAsync(
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var filterOptions = new MemoryFilterOptions
        {
            Tiers = options.Tiers?.ToArray(),
            Types = options.Types?.ToArray(),
            IncludeDeleted = options.IncludeDeleted
        };

        if (options.UserId != null)
        {
            return await _memoryStore.GetAllAsync(options.UserId, filterOptions, cancellationToken);
        }

        // For all users export, we need to iterate (storage doesn't support cross-user query)
        // This is a simplified implementation - production would need pagination
        _logger.LogWarning("Exporting all users is not fully supported - using current user context");
        return [];
    }

    private static List<MemoryUnit> FilterMemories(IReadOnlyList<MemoryUnit> memories, ExportOptions options)
    {
        IEnumerable<MemoryUnit> filtered = memories;

        if (options.Since.HasValue)
        {
            var sinceUtc = options.Since.Value.UtcDateTime;
            filtered = filtered.Where(m => m.UpdatedAt >= sinceUtc || m.CreatedAt >= sinceUtc);
        }

        if (options.Until.HasValue)
        {
            var untilUtc = options.Until.Value.UtcDateTime;
            filtered = filtered.Where(m => m.CreatedAt <= untilUtc);
        }

        if (options.SessionId != null)
        {
            filtered = filtered.Where(m => m.SessionId == options.SessionId);
        }

        return filtered.ToList();
    }

    private static IReadOnlyList<MemoryUnit> PrepareMemoriesForExport(
        List<MemoryUnit> memories,
        ExportOptions options)
    {
        if (options.IncludeEmbeddings && options.IncludeMetadata)
        {
            return memories;
        }

        // Create copies without embeddings/metadata if excluded
        return memories.Select(m =>
        {
            var copy = new MemoryUnit
            {
                Id = m.Id,
                UserId = m.UserId,
                SessionId = m.SessionId,
                Content = m.Content,
                ContentHash = m.ContentHash,
                Type = m.Type,
                Tier = m.Tier,
                Scope = m.Scope,
                Confidence = m.Confidence,
                ConfirmCount = m.ConfirmCount,
                ImportanceScore = m.ImportanceScore,
                RetentionScore = m.RetentionScore,
                Stability = m.Stability,
                AccessCount = m.AccessCount,
                ActivationCount = m.ActivationCount,
                TopicId = m.TopicId,
                Topics = m.Topics,
                Entities = m.Entities,
                SupersedesId = m.SupersedesId,
                IsDeleted = m.IsDeleted,
                IsLocked = m.IsLocked,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
                LastAccessedAt = m.LastAccessedAt,
                ExpiresAt = m.ExpiresAt,
                Embedding = options.IncludeEmbeddings ? m.Embedding : default,
                Metadata = options.IncludeMetadata ? m.Metadata : null
            };
            return copy;
        }).ToList();
    }

    private static ExportStatistics CalculateStatistics(List<MemoryUnit> memories)
    {
        var stats = new ExportStatistics
        {
            TotalMemories = memories.Count,
            UniqueUsers = memories.Select(m => m.UserId).Distinct().Count(),
            UniqueSessions = memories.Select(m => m.SessionId).Where(s => s != null).Distinct().Count()
        };

        if (memories.Count > 0)
        {
            stats.EarliestMemory = memories.Min(m => m.CreatedAt);
            stats.LatestMemory = memories.Max(m => m.UpdatedAt);
        }

        stats.ByTier = memories
            .GroupBy(m => m.Tier)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.ByType = memories
            .GroupBy(m => m.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return stats;
    }

    private static MemoryExportPackage ClonePackageWithoutChecksum(MemoryExportPackage package)
    {
        return new MemoryExportPackage
        {
            FormatVersion = package.FormatVersion,
            ExportedAt = package.ExportedAt,
            SourceSystem = package.SourceSystem,
            Options = package.Options,
            Memories = package.Memories,
            Statistics = package.Statistics,
            Checksum = null
        };
    }

    private static string CalculateChecksum(MemoryExportPackage package)
    {
        var content = JsonSerializer.Serialize(new
        {
            package.Memories.Count,
            package.ExportedAt,
            MemoryIds = package.Memories.Select(m => m.Id.ToString()).OrderBy(id => id)
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }

    private static void ValidatePackage(MemoryExportPackage package, List<ImportError> errors)
    {
        for (var i = 0; i < package.Memories.Count; i++)
        {
            var memory = package.Memories[i];

            if (string.IsNullOrWhiteSpace(memory.Content))
            {
                errors.Add(new ImportError
                {
                    MemoryId = memory.Id,
                    Index = i,
                    Message = "Memory content is empty"
                });
            }

            if (string.IsNullOrWhiteSpace(memory.UserId))
            {
                errors.Add(new ImportError
                {
                    MemoryId = memory.Id,
                    Index = i,
                    Message = "Memory userId is required"
                });
            }
        }
    }

    private async Task ProcessImportBatchAsync(
        IEnumerable<(MemoryUnit Memory, int Index)> batch,
        ImportOptions options,
        List<ImportConflict> conflicts,
        List<ImportError> errors,
        Dictionary<Guid, Guid> idMapping,
        ImportResult result,
        CancellationToken cancellationToken)
    {
        foreach (var (memory, index) in batch)
        {
            try
            {
                await ProcessSingleImportAsync(
                    memory,
                    index,
                    options,
                    conflicts,
                    idMapping,
                    result,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add(new ImportError
                {
                    MemoryId = memory.Id,
                    Index = index,
                    Message = ex.Message
                });
                result.FailedCount++;

                if (options.ConflictResolution == ImportConflictResolution.Fail)
                {
                    throw;
                }
            }
        }
    }

    private async Task ProcessSingleImportAsync(
        MemoryUnit memory,
        int index,
        ImportOptions options,
        List<ImportConflict> conflicts,
        Dictionary<Guid, Guid> idMapping,
        ImportResult result,
        CancellationToken cancellationToken)
    {
        // Apply overrides
        var importMemory = PrepareMemoryForImport(memory, options);

        // Check for existing memory
        var existingMemory = await _memoryStore.GetByIdAsync(importMemory.Id, cancellationToken);

        if (existingMemory != null)
        {
            var resolution = ResolveConflict(existingMemory, importMemory, options.ConflictResolution);
            
            conflicts.Add(new ImportConflict
            {
                MemoryId = memory.Id,
                ImportedContent = Truncate(importMemory.Content, 100),
                ExistingContent = Truncate(existingMemory.Content, 100),
                Resolution = options.ConflictResolution,
                Outcome = resolution.Outcome
            });

            if (resolution.ShouldSkip)
            {
                result.SkippedCount++;
                return;
            }

            if (resolution.ShouldReplace)
            {
                await _memoryStore.UpdateAsync(importMemory, cancellationToken);
                result.ReplacedCount++;
                return;
            }
        }

        // Generate new ID if not preserving
        if (!options.PreserveIds)
        {
            var originalId = importMemory.Id;
            importMemory.Id = Guid.NewGuid();
            idMapping[originalId] = importMemory.Id;
        }

        // Regenerate embeddings if requested
        if (options.RegenerateEmbeddings && _embeddingService != null)
        {
            importMemory.Embedding = await _embeddingService.GenerateEmbeddingAsync(
                importMemory.Content,
                cancellationToken);
        }

        // Store the memory
        await _memoryStore.StoreAsync(importMemory, cancellationToken);
        result.ImportedCount++;
    }

    private static MemoryUnit PrepareMemoryForImport(MemoryUnit memory, ImportOptions options)
    {
        var copy = new MemoryUnit
        {
            Id = memory.Id,
            UserId = options.OverrideUserId ?? memory.UserId,
            SessionId = options.OverrideSessionId ?? memory.SessionId,
            Content = memory.Content,
            ContentHash = memory.ContentHash,
            Type = memory.Type,
            Tier = memory.Tier,
            Scope = memory.Scope,
            Confidence = memory.Confidence,
            ConfirmCount = memory.ConfirmCount,
            ImportanceScore = memory.ImportanceScore,
            RetentionScore = memory.RetentionScore,
            Stability = memory.Stability,
            AccessCount = memory.AccessCount,
            ActivationCount = memory.ActivationCount,
            TopicId = memory.TopicId,
            Topics = memory.Topics,
            Entities = memory.Entities,
            SupersedesId = memory.SupersedesId,
            IsDeleted = memory.IsDeleted,
            IsLocked = memory.IsLocked,
            Embedding = memory.Embedding,
            Metadata = memory.Metadata
        };

        if (options.PreserveTimestamps)
        {
            copy.CreatedAt = memory.CreatedAt;
            copy.UpdatedAt = memory.UpdatedAt;
            copy.LastAccessedAt = memory.LastAccessedAt;
            copy.ExpiresAt = memory.ExpiresAt;
        }
        else
        {
            copy.CreatedAt = DateTime.UtcNow;
            copy.UpdatedAt = DateTime.UtcNow;
        }

        return copy;
    }

    private static (bool ShouldSkip, bool ShouldReplace, string Outcome) ResolveConflict(
        MemoryUnit existing,
        MemoryUnit imported,
        ImportConflictResolution resolution)
    {
        return resolution switch
        {
            ImportConflictResolution.Skip => (true, false, "Kept existing"),
            ImportConflictResolution.Replace => (false, true, "Replaced with imported"),
            ImportConflictResolution.KeepNewer => existing.UpdatedAt >= imported.UpdatedAt
                ? (true, false, "Kept existing (newer)")
                : (false, true, "Replaced with imported (newer)"),
            ImportConflictResolution.KeepHigherConfidence => existing.Confidence >= imported.Confidence
                ? (true, false, "Kept existing (higher confidence)")
                : (false, true, "Replaced with imported (higher confidence)"),
            ImportConflictResolution.Fail => throw new InvalidOperationException(
                $"Conflict detected for memory {imported.Id}"),
            _ => (true, false, "Kept existing (default)")
        };
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (text == null || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }
}
