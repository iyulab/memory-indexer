using System.Text.Json;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MemoryIndexer.Storage.Qdrant;

/// <summary>
/// Qdrant-based implementation of IMemoryStore for production-scale vector storage.
/// </summary>
public sealed class QdrantMemoryStore : IMemoryStore, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantMemoryStore> _logger;
    private readonly string _collectionName;
    private readonly int _vectorDimensions;
    private bool _initialized;

    private const string DefaultCollectionName = "memories";
    private const int DefaultVectorDimensions = 1024;

    public QdrantMemoryStore(
        string host = "localhost",
        int port = 6334,
        string? apiKey = null,
        string collectionName = DefaultCollectionName,
        int vectorDimensions = DefaultVectorDimensions,
        ILogger<QdrantMemoryStore>? logger = null)
    {
        _client = apiKey != null
            ? new QdrantClient(host, port, apiKey: apiKey)
            : new QdrantClient(host, port);
        _collectionName = collectionName;
        _vectorDimensions = vectorDimensions;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QdrantMemoryStore>.Instance;
    }

    public QdrantMemoryStore(
        QdrantClient client,
        string collectionName = DefaultCollectionName,
        int vectorDimensions = DefaultVectorDimensions,
        ILogger<QdrantMemoryStore>? logger = null)
    {
        _client = client;
        _collectionName = collectionName;
        _vectorDimensions = vectorDimensions;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QdrantMemoryStore>.Instance;
    }

    /// <inheritdoc />
    public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken);
            if (!collections.Any(c => c == _collectionName))
            {
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams
                    {
                        Size = (ulong)_vectorDimensions,
                        Distance = Distance.Cosine
                    },
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Created Qdrant collection {Collection} with {Dimensions} dimensions",
                    _collectionName, _vectorDimensions);
            }

            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Qdrant collection {Collection}", _collectionName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteCollectionAsync(_collectionName, timeout: null, cancellationToken);
            _initialized = false;
            _logger.LogInformation("Deleted Qdrant collection {Collection}", _collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Qdrant collection {Collection}", _collectionName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        if (!memory.Embedding.HasValue)
        {
            throw new ArgumentException("Memory must have an embedding for Qdrant storage", nameof(memory));
        }

        var point = CreatePointFromMemory(memory);
        await _client.UpsertAsync(_collectionName, new[] { point }, cancellationToken: cancellationToken);

        _logger.LogDebug("Stored memory {MemoryId} in Qdrant collection {Collection}",
            memory.Id, _collectionName);

        return memory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(
        IEnumerable<MemoryUnit> memories,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var memoryList = memories.ToList();
        var points = new List<PointStruct>();

        foreach (var memory in memoryList)
        {
            if (!memory.Embedding.HasValue)
            {
                throw new ArgumentException("All memories must have embeddings for Qdrant storage", nameof(memories));
            }
            points.Add(CreatePointFromMemory(memory));
        }

        await _client.UpsertAsync(_collectionName, points, cancellationToken: cancellationToken);

        _logger.LogDebug("Stored {Count} memories in Qdrant collection {Collection}",
            memoryList.Count, _collectionName);

        return memoryList;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        try
        {
            var points = await _client.RetrieveAsync(
                _collectionName,
                new[] { new PointId { Uuid = id.ToString() } },
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            var point = points.FirstOrDefault();
            return point != null ? PointToMemory(point) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get memory {MemoryId} from Qdrant", id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        try
        {
            var pointIds = ids.Select(id => new PointId { Uuid = id.ToString() }).ToList();
            var points = await _client.RetrieveAsync(
                _collectionName,
                pointIds,
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            return points.Select(PointToMemory).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get memories from Qdrant");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GetAllAsync(
        string userId,
        MemoryFilterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var filter = BuildUserFilter(userId, options);
        var limit = (uint)(options?.Limit ?? 10000);

        var result = await _client.ScrollAsync(
            _collectionName,
            filter: filter,
            limit: limit,
            payloadSelector: true,
            vectorsSelector: true,
            cancellationToken: cancellationToken);

        return result.Result.Select(PointToMemory).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var filter = BuildSearchFilter(options);

        var searchResult = await _client.SearchAsync(
            _collectionName,
            queryEmbedding.ToArray(),
            filter: filter,
            limit: (ulong)options.Limit,
            payloadSelector: true,
            vectorsSelector: true,
            cancellationToken: cancellationToken);

        return searchResult
            .Where(r => r.Score >= options.MinScore)
            .Select(r => new MemorySearchResult
            {
                Memory = PointToMemory(r),
                Score = r.Score
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        try
        {
            // Check if exists
            var existing = await GetByIdAsync(memory.Id, cancellationToken);
            if (existing == null) return false;

            memory.UpdatedAt = DateTime.UtcNow;

            if (!memory.Embedding.HasValue)
            {
                throw new ArgumentException("Memory must have an embedding for Qdrant storage", nameof(memory));
            }

            var point = CreatePointFromMemory(memory);
            await _client.UpsertAsync(_collectionName, new[] { point }, cancellationToken: cancellationToken);

            _logger.LogDebug("Updated memory {MemoryId} in Qdrant", memory.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update memory {MemoryId} in Qdrant", memory.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        try
        {
            if (hardDelete)
            {
                // Delete by ID using Guid
                await _client.DeleteAsync(
                    _collectionName,
                    ids: new[] { id },
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Hard deleted memory {MemoryId} from Qdrant", id);
            }
            else
            {
                // Soft delete - update the is_deleted flag
                var existing = await GetByIdAsync(id, cancellationToken);
                if (existing == null) return false;

                existing.IsDeleted = true;
                await UpdateAsync(existing, cancellationToken);

                _logger.LogDebug("Soft deleted memory {MemoryId} in Qdrant", id);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete memory {MemoryId} from Qdrant", id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<long> GetCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "user_id",
                        Match = new Match { Keyword = userId }
                    }
                }
            }
        };

        var countResult = await _client.CountAsync(_collectionName, filter: filter, cancellationToken: cancellationToken);
        return (long)countResult;
    }

    /// <summary>
    /// Gets collection information including health status.
    /// </summary>
    public async Task<QdrantHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await _client.GetCollectionInfoAsync(_collectionName, cancellationToken);
            return new QdrantHealthInfo
            {
                IsHealthy = info.Status == CollectionStatus.Green,
                CollectionName = _collectionName,
                PointsCount = (long)info.PointsCount,
                VectorsCount = (long)info.PointsCount, // VectorsCount removed in newer API
                Status = info.Status.ToString()
            };
        }
        catch (Exception ex)
        {
            return new QdrantHealthInfo
            {
                IsHealthy = false,
                CollectionName = _collectionName,
                Status = "Error",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Migrates data from another memory store.
    /// </summary>
    public async Task MigrateFromAsync(
        IMemoryStore sourceStore,
        string userId,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var memories = await sourceStore.GetAllAsync(userId, cancellationToken: cancellationToken);
        var total = memories.Count;
        var processed = 0;

        foreach (var memory in memories)
        {
            await StoreAsync(memory, cancellationToken);
            processed++;
            progressCallback?.Invoke(processed, total);
        }

        _logger.LogInformation("Migrated {Count} memories for user {UserId} to Qdrant",
            total, userId);
    }

    private static PointStruct CreatePointFromMemory(MemoryUnit memory)
    {
        return new PointStruct
        {
            Id = new PointId { Uuid = memory.Id.ToString() },
            Vectors = memory.Embedding!.Value.ToArray(),
            Payload =
            {
                ["user_id"] = memory.UserId ?? string.Empty,
                ["session_id"] = memory.SessionId ?? string.Empty,
                ["content"] = memory.Content,
                ["content_hash"] = memory.ContentHash ?? string.Empty,
                ["type"] = (int)memory.Type,
                ["importance_score"] = memory.ImportanceScore,
                ["access_count"] = memory.AccessCount,
                ["created_at"] = memory.CreatedAt.ToString("O"),
                ["updated_at"] = memory.UpdatedAt.ToString("O"),
                ["last_accessed_at"] = memory.LastAccessedAt?.ToString("O") ?? string.Empty,
                ["is_deleted"] = memory.IsDeleted,
                ["topics"] = JsonSerializer.Serialize(memory.Topics),
                ["entities"] = JsonSerializer.Serialize(memory.Entities),
                ["metadata"] = JsonSerializer.Serialize(memory.Metadata)
            }
        };
    }

    private static Filter BuildUserFilter(string userId, MemoryFilterOptions? options)
    {
        var conditions = new List<Condition>
        {
            new Condition
            {
                Field = new FieldCondition
                {
                    Key = "user_id",
                    Match = new Match { Keyword = userId }
                }
            }
        };

        // Exclude deleted unless requested
        if (options?.IncludeDeleted != true)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "is_deleted",
                    Match = new Match { Boolean = false }
                }
            });
        }

        if (!string.IsNullOrEmpty(options?.SessionId))
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "session_id",
                    Match = new Match { Keyword = options.SessionId }
                }
            });
        }

        if (options?.Types?.Length > 0)
        {
            AddTypeConditions(conditions, options.Types);
        }

        return new Filter { Must = { conditions } };
    }

    private static Filter? BuildSearchFilter(MemorySearchOptions options)
    {
        var conditions = new List<Condition>();

        if (!string.IsNullOrEmpty(options.UserId))
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "user_id",
                    Match = new Match { Keyword = options.UserId }
                }
            });
        }

        if (!string.IsNullOrEmpty(options.SessionId))
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "session_id",
                    Match = new Match { Keyword = options.SessionId }
                }
            });
        }

        // Exclude deleted unless requested
        if (!options.IncludeDeleted)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "is_deleted",
                    Match = new Match { Boolean = false }
                }
            });
        }

        if (options.Types?.Length > 0)
        {
            AddTypeConditions(conditions, options.Types);
        }

        if (conditions.Count == 0)
            return null;

        return new Filter { Must = { conditions } };
    }

    private static void AddTypeConditions(List<Condition> conditions, MemoryType[] types)
    {
        if (types.Length == 1)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "type",
                    Match = new Match { Integer = (int)types[0] }
                }
            });
        }
        else
        {
            // For multiple types, add each as OR condition using nested filter
            var typeFilter = new Filter();
            foreach (var t in types)
            {
                typeFilter.Should.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "type",
                        Match = new Match { Integer = (int)t }
                    }
                });
            }
            conditions.Add(new Condition { Filter = typeFilter });
        }
    }

    private static MemoryUnit PointToMemory(RetrievedPoint point)
        => PayloadToMemory(point.Id.Uuid, point.Payload, point.Vectors);

    private static MemoryUnit PointToMemory(ScoredPoint point)
        => PayloadToMemory(point.Id.Uuid, point.Payload, point.Vectors);

    private static MemoryUnit PayloadToMemory(
        string uuid,
        IDictionary<string, Value> payload,
        VectorsOutput? vectors)
    {
        return new MemoryUnit
        {
            Id = Guid.Parse(uuid),
            UserId = GetPayloadString(payload, "user_id"),
            SessionId = GetPayloadStringOrNull(payload, "session_id"),
            Content = GetPayloadString(payload, "content"),
            ContentHash = GetPayloadStringOrNull(payload, "content_hash"),
            Type = (MemoryType)GetPayloadInt(payload, "type"),
            ImportanceScore = GetPayloadFloat(payload, "importance_score"),
            AccessCount = GetPayloadInt(payload, "access_count"),
            CreatedAt = GetPayloadDateTime(payload, "created_at"),
            UpdatedAt = GetPayloadDateTime(payload, "updated_at"),
            LastAccessedAt = GetPayloadDateTimeOrNull(payload, "last_accessed_at"),
            IsDeleted = GetPayloadBool(payload, "is_deleted"),
            Embedding = ExtractEmbedding(vectors),
            Topics = GetPayloadList(payload, "topics"),
            Entities = GetPayloadList(payload, "entities"),
            Metadata = GetPayloadStringDictionary(payload, "metadata")
        };
    }

    private static float[]? ExtractEmbedding(VectorsOutput? vectors)
    {
        if (vectors == null) return null;

        // Try to get the vector data from the new API
        var vector = vectors.Vector;
        if (vector == null) return null;

#pragma warning disable CS0612 // Type or member is obsolete
        // Use Data property (deprecated but still available)
        return vector.Data?.ToArray();
#pragma warning restore CS0612
    }

    private static string GetPayloadString(
        IDictionary<string, Value> payload,
        string key,
        string defaultValue = "")
    {
        return payload.TryGetValue(key, out var value) ? value.StringValue ?? defaultValue : defaultValue;
    }

    private static string? GetPayloadStringOrNull(IDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value)) return null;
        var str = value.StringValue;
        return string.IsNullOrEmpty(str) ? null : str;
    }

    private static int GetPayloadInt(IDictionary<string, Value> payload, string key, int defaultValue = 0)
    {
        return payload.TryGetValue(key, out var value) ? (int)value.IntegerValue : defaultValue;
    }

    private static float GetPayloadFloat(IDictionary<string, Value> payload, string key, float defaultValue = 0)
    {
        return payload.TryGetValue(key, out var value) ? (float)value.DoubleValue : defaultValue;
    }

    private static bool GetPayloadBool(IDictionary<string, Value> payload, string key, bool defaultValue = false)
    {
        return payload.TryGetValue(key, out var value) ? value.BoolValue : defaultValue;
    }

    private static DateTime GetPayloadDateTime(IDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value.StringValue))
        {
            return DateTime.Parse(value.StringValue);
        }
        return DateTime.MinValue;
    }

    private static DateTime? GetPayloadDateTimeOrNull(IDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value.StringValue))
        {
            return DateTime.TryParse(value.StringValue, out var dt) ? dt : null;
        }
        return null;
    }

    private static List<string> GetPayloadList(IDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value.StringValue))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(value.StringValue) ?? [];
            }
            catch
            {
                return [];
            }
        }
        return [];
    }

    private static Dictionary<string, string> GetPayloadStringDictionary(IDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value.StringValue))
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(value.StringValue) ?? [];
            }
            catch
            {
                return [];
            }
        }
        return [];
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Health information for Qdrant collection.
/// </summary>
public sealed class QdrantHealthInfo
{
    public bool IsHealthy { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    public long PointsCount { get; set; }
    public long VectorsCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
