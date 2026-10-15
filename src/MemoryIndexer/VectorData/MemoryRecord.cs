using System.Globalization;
using System.Text.Json;
using MemoryIndexer.Models;
using Microsoft.Extensions.VectorData;

namespace MemoryIndexer.VectorData;

/// <summary>
/// How a <see cref="MemoryUnit"/> is stored in a Microsoft.Extensions.VectorData collection by
/// <see cref="VectorStoreMemoryStore"/>: one row of property types every connector stores and filters on — strings,
/// numbers, booleans and string lists. Enums are stored by name, times as UTC ticks, the metadata dictionary as JSON plus
/// one <c>key=value</c> entry per pair (so a metadata filter is a list lookup the database runs).
/// </summary>
/// <remarks>
/// The collection's schema is <see cref="CreateDefinition"/>, built from the configured embedding dimension — this type
/// carries no attributes, so no dimension is fixed in code.
/// </remarks>
public sealed class MemoryRecord
{
    /// <summary>Separates key and value in <see cref="MetadataPairs"/> (a unit separator, which neither contains).</summary>
    public const char MetadataPairSeparator = '\u001f';

    /// <summary>Memory id (the key).</summary>
    public Guid Id { get; set; }

    /// <summary>Owner (indexed).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Session (indexed).</summary>
    public string? SessionId { get; set; }

    /// <summary>Namespace (indexed).</summary>
    public string? Namespace { get; set; }

    /// <summary>Speaker role (indexed).</summary>
    public string? Role { get; set; }

    /// <summary>Memory text.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Embedding (the vector property). <see cref="VectorStoreMemoryStore"/> writes a placeholder for a memory without one
    /// (some connectors refuse a point without a vector) and marks it with <see cref="HasEmbedding"/>.
    /// </summary>
    public ReadOnlyMemory<float>? Embedding { get; set; }

    /// <summary>Whether <see cref="Embedding"/> is the memory's own (indexed — a search only matches those).</summary>
    public bool HasEmbedding { get; set; }

    /// <summary>Creation time, UTC ticks (indexed — date filters and ordering).</summary>
    public long CreatedAtTicks { get; set; }

    /// <summary>Last update, UTC ticks (indexed — ordering).</summary>
    public long UpdatedAtTicks { get; set; }

    /// <summary>Last access, UTC ticks.</summary>
    public long? LastAccessedAtTicks { get; set; }

    /// <summary>Importance (indexed — ordering).</summary>
    public double ImportanceScore { get; set; }

    /// <summary>Access count (indexed — ordering).</summary>
    public int AccessCount { get; set; }

    /// <summary><see cref="MemoryType"/> name (indexed).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary><see cref="Tier"/> name (indexed).</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary><see cref="Scope"/> name (indexed).</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Topic id.</summary>
    public string? TopicId { get; set; }

    /// <summary>Confidence.</summary>
    public double Confidence { get; set; }

    /// <summary>Confirmations.</summary>
    public int ConfirmCount { get; set; }

    /// <summary>Activations.</summary>
    public int ActivationCount { get; set; }

    /// <summary><see cref="MemoryStability"/> name.</summary>
    public string Stability { get; set; } = string.Empty;

    /// <summary>Retention score.</summary>
    public double RetentionScore { get; set; }

    /// <summary>Content hash.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Topics.</summary>
    public List<string>? Topics { get; set; }

    /// <summary>Entities.</summary>
    public List<string>? Entities { get; set; }

    /// <summary>The metadata dictionary as JSON.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>One <c>key{separator}value</c> entry per metadata pair, empty when there is none (indexed — metadata filters).</summary>
    public List<string> MetadataPairs { get; set; } = [];

    /// <summary>Soft-deleted (indexed).</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Locked against automatic changes.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Expiry, UTC ticks.</summary>
    public long? ExpiresAtTicks { get; set; }

    /// <summary>Id of the memory this one supersedes.</summary>
    public string? SupersedesId { get; set; }

    /// <summary>Confidence score.</summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>The <c>key{separator}value</c> entry a metadata filter looks up.</summary>
    public static string MetadataPair(string key, string value) => key + MetadataPairSeparator + value;

    /// <summary>
    /// The collection schema: key, data properties (the filtered ones indexed), and the embedding with
    /// <paramref name="dimensions"/> and <paramref name="distanceFunction"/>.
    /// </summary>
    public static VectorStoreCollectionDefinition CreateDefinition(int dimensions, string distanceFunction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(distanceFunction);

        static VectorStoreDataProperty Data<T>(string name, bool indexed = false) => new(name, typeof(T)) { IsIndexed = indexed };

        return new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty(nameof(Id), typeof(Guid)),
                Data<string>(nameof(UserId), indexed: true),
                Data<string>(nameof(SessionId), indexed: true),
                Data<string>(nameof(Namespace), indexed: true),
                Data<string>(nameof(Role), indexed: true),
                Data<string>(nameof(Content)),
                new VectorStoreVectorProperty(nameof(Embedding), typeof(ReadOnlyMemory<float>?), dimensions)
                {
                    DistanceFunction = distanceFunction
                },
                Data<long>(nameof(CreatedAtTicks), indexed: true),
                Data<long>(nameof(UpdatedAtTicks), indexed: true),
                Data<long?>(nameof(LastAccessedAtTicks)),
                Data<double>(nameof(ImportanceScore), indexed: true),
                Data<int>(nameof(AccessCount), indexed: true),
                Data<string>(nameof(Type), indexed: true),
                Data<string>(nameof(Tier), indexed: true),
                Data<string>(nameof(Scope), indexed: true),
                Data<string>(nameof(TopicId)),
                Data<double>(nameof(Confidence)),
                Data<int>(nameof(ConfirmCount)),
                Data<int>(nameof(ActivationCount)),
                Data<string>(nameof(Stability)),
                Data<double>(nameof(RetentionScore)),
                Data<string>(nameof(ContentHash)),
                Data<List<string>>(nameof(Topics)),
                Data<List<string>>(nameof(Entities)),
                Data<string>(nameof(MetadataJson)),
                Data<List<string>>(nameof(MetadataPairs), indexed: true),
                Data<bool>(nameof(HasEmbedding), indexed: true),
                Data<bool>(nameof(IsDeleted), indexed: true),
                Data<bool>(nameof(IsLocked)),
                Data<long?>(nameof(ExpiresAtTicks)),
                Data<string>(nameof(SupersedesId)),
                Data<double?>(nameof(ConfidenceScore))
            ]
        };
    }

    /// <summary>The record for <paramref name="memory"/>.</summary>
    public static MemoryRecord From(MemoryUnit memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return new MemoryRecord
        {
            Id = memory.Id,
            UserId = memory.UserId,
            SessionId = memory.SessionId,
            Namespace = memory.Namespace,
            Role = memory.Role,
            Content = memory.Content,
            Embedding = memory.Embedding,
            HasEmbedding = memory.Embedding is not null,
            CreatedAtTicks = Ticks(memory.CreatedAt),
            UpdatedAtTicks = Ticks(memory.UpdatedAt),
            LastAccessedAtTicks = memory.LastAccessedAt is { } accessed ? Ticks(accessed) : null,
            ImportanceScore = memory.ImportanceScore,
            AccessCount = memory.AccessCount,
            Type = memory.Type.ToString(),
            Tier = memory.Tier.ToString(),
            Scope = memory.Scope.ToString(),
            TopicId = memory.TopicId,
            Confidence = memory.Confidence,
            ConfirmCount = memory.ConfirmCount,
            ActivationCount = memory.ActivationCount,
            Stability = memory.Stability.ToString(),
            RetentionScore = memory.RetentionScore,
            ContentHash = memory.ContentHash,
            Topics = memory.Topics is null ? null : [.. memory.Topics],
            Entities = memory.Entities is null ? null : [.. memory.Entities],
            MetadataJson = memory.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(memory.Metadata) : null,
            MetadataPairs = memory.Metadata is { Count: > 0 } ? [.. memory.Metadata.Select(kv => MetadataPair(kv.Key, kv.Value))] : [],
            IsDeleted = memory.IsDeleted,
            IsLocked = memory.IsLocked,
            ExpiresAtTicks = memory.ExpiresAt is { } expires ? Ticks(expires) : null,
            SupersedesId = memory.SupersedesId?.ToString(),
            ConfidenceScore = memory.ConfidenceScore
        };
    }

    /// <summary>The memory this record stores.</summary>
    public MemoryUnit ToMemory() => new()
    {
        Id = Id,
        UserId = UserId,
        SessionId = SessionId,
        Namespace = Namespace,
        Role = Role,
        Content = Content,
        Embedding = HasEmbedding ? Embedding : null,
        CreatedAt = FromTicks(CreatedAtTicks),
        UpdatedAt = FromTicks(UpdatedAtTicks),
        LastAccessedAt = LastAccessedAtTicks is { } accessed ? FromTicks(accessed) : null,
        ImportanceScore = (float)ImportanceScore,
        AccessCount = AccessCount,
        Type = Enum.Parse<MemoryType>(Type),
        Tier = Enum.Parse<Tier>(Tier),
        Scope = Enum.Parse<Scope>(Scope),
        TopicId = TopicId,
        Confidence = (float)Confidence,
        ConfirmCount = ConfirmCount,
        ActivationCount = ActivationCount,
        Stability = Enum.Parse<MemoryStability>(Stability),
        RetentionScore = (float)RetentionScore,
        ContentHash = ContentHash,
        Topics = Topics is null ? null : [.. Topics],
        Entities = Entities is null ? null : [.. Entities],
        Metadata = MetadataJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson),
        IsDeleted = IsDeleted,
        IsLocked = IsLocked,
        ExpiresAt = ExpiresAtTicks is { } expires ? FromTicks(expires) : null,
        SupersedesId = SupersedesId is null ? null : Guid.Parse(SupersedesId, CultureInfo.InvariantCulture),
        ConfidenceScore = ConfidenceScore is { } score ? (float)score : null
    };

    /// <summary>UTC ticks of <paramref name="time"/> (an unspecified kind is read as UTC).</summary>
    public static long Ticks(DateTime time)
        => (time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time).Ticks;

    private static DateTime FromTicks(long ticks) => new(ticks, DateTimeKind.Utc);
}
