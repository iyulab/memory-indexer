using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Profile;

/// <summary>
/// JSON profile exporter for GDPR compliance.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public partial class JsonProfileExporter : IProfileExporter
{
    private readonly IArchiveStore _archiveStore;
    private readonly ILogger<JsonProfileExporter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <inheritdoc />
    public string Format => "json";

    public JsonProfileExporter(
        IArchiveStore archiveStore,
        ILogger<JsonProfileExporter> logger)
    {
        _archiveStore = archiveStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProfileExportResult> ExportAsync(
        string userId,
        ProfileExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            options ??= new ProfileExportOptions();

            // Get all facts
            var allFacts = await _archiveStore.GetAllAsync(userId, cancellationToken);
            var factsList = allFacts.ToList();

            // Apply filters
            var filteredFacts = ApplyFilters(factsList, options);

            // Separate active and archived
            var activeFacts = filteredFacts.Where(f => f.IsActive).ToList();
            var archivedFacts = options.IncludeArchived
                ? filteredFacts.Where(f => !f.IsActive).ToList()
                : null;

            // Build metadata
            var metadata = BuildMetadata(userId, activeFacts, archivedFacts, options);

            // Convert to exported format
            var exportedActiveFacts = activeFacts.Select(f => ToExportedFact(f, options)).ToList();
            var exportedArchivedFacts = archivedFacts?.Select(f => ToExportedFact(f, options)).ToList();

            // Build export object
            var export = new ExportedProfile
            {
                Metadata = metadata,
                ActiveFacts = exportedActiveFacts,
                ArchivedFacts = exportedArchivedFacts
            };

            // Serialize to JSON
            var json = JsonSerializer.Serialize(export, JsonOptions);

            // Calculate checksum
            metadata.Checksum = ComputeChecksum(json);
            metadata.SizeBytes = Encoding.UTF8.GetByteCount(json);

            // Re-serialize with checksum
            json = JsonSerializer.Serialize(export, JsonOptions);

            LogExportedProfileUserUserIdFactCount(_logger, userId, metadata.FactCount, metadata.SizeBytes);

            return new ProfileExportResult
            {
                Success = true,
                Metadata = metadata,
                Data = json
            };
        }
        catch (Exception ex)
        {
            LogFailedExportProfileUserUserId(_logger, ex, userId);
            return new ProfileExportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<ProfileExportMetadata> ExportToStreamAsync(
        string userId,
        Stream outputStream,
        ProfileExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ExportAsync(userId, options, cancellationToken);

        if (!result.Success || result.Data == null)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Export failed");
        }

        var bytes = Encoding.UTF8.GetBytes(result.Data);
        await outputStream.WriteAsync(bytes, cancellationToken);

        return result.Metadata!;
    }

    #region Helper Methods

    private static List<SemanticStoreEntry> ApplyFilters(
        List<SemanticStoreEntry> facts,
        ProfileExportOptions options)
    {
        var filtered = facts.AsEnumerable();

        // Category filters
        if (options.IncludeCategories != null && options.IncludeCategories.Count > 0)
        {
            var includeSet = options.IncludeCategories.ToHashSet();
            filtered = filtered.Where(f => includeSet.Contains(f.Category));
        }

        if (options.ExcludeCategories != null && options.ExcludeCategories.Count > 0)
        {
            var excludeSet = options.ExcludeCategories.ToHashSet();
            filtered = filtered.Where(f => !excludeSet.Contains(f.Category));
        }

        // Date range filters
        if (options.Since.HasValue)
        {
            filtered = filtered.Where(f => f.UpdatedAt >= options.Since.Value);
        }

        if (options.Until.HasValue)
        {
            filtered = filtered.Where(f => f.UpdatedAt <= options.Until.Value);
        }

        return filtered.ToList();
    }

    private static ProfileExportMetadata BuildMetadata(
        string userId,
        List<SemanticStoreEntry> activeFacts,
        List<SemanticStoreEntry>? archivedFacts,
        ProfileExportOptions options)
    {
        var allFacts = activeFacts.Concat(archivedFacts ?? []).ToList();

        var factsByCategory = allFacts
            .GroupBy(f => f.Category.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new ProfileExportMetadata
        {
            UserId = userId,
            Format = "json",
            FactCount = allFacts.Count,
            ActiveFactCount = activeFacts.Count,
            ArchivedFactCount = archivedFacts?.Count ?? 0,
            FactsByCategory = factsByCategory,
            EarliestFact = allFacts.Min(f => (DateTime?)f.CreatedAt),
            LatestFact = allFacts.Max(f => (DateTime?)f.UpdatedAt),
            Options = options
        };
    }

    private static ExportedFact ToExportedFact(SemanticStoreEntry entry, ProfileExportOptions options)
    {
        var value = entry.Value;

        // Apply redaction if enabled
        if (options.RedactSensitive && options.RedactionPatterns != null)
        {
            foreach (var pattern in options.RedactionPatterns)
            {
                value = System.Text.RegularExpressions.Regex.Replace(
                    value, pattern, "[REDACTED]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
        }

        var exported = new ExportedFact
        {
            Key = entry.Key,
            Value = value,
            Category = entry.Category.ToString(),
            Confidence = entry.Confidence,
            ConfirmationCount = entry.ConfirmationCount,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
            ValidFrom = entry.ValidFrom,
            ValidTo = entry.ValidTo,
            IsActive = entry.IsActive,
            Version = entry.Version,
            SupersedesKey = entry.SupersedesKey,
            SourceSessions = entry.SourceSessions.Count > 0 ? entry.SourceSessions.ToList() : null
        };

        // Include metadata if requested
        if (options.IncludeMetadata && entry.Metadata.Count > 0)
        {
            exported.Metadata = entry.Metadata;
        }

        return exported;
    }

    private static string ComputeChecksum(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Information, Message = "Exported profile for user {UserId}: {FactCount} facts, {Size} bytes")]
    private static partial void LogExportedProfileUserUserIdFactCount(ILogger logger, string userId, int factCount, long size);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to export profile for user {UserId}")]
    private static partial void LogFailedExportProfileUserUserId(ILogger logger, Exception ex, string userId);
}
