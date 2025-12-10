using System.Text.Json;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Storage.Sqlite;

/// <summary>
/// SQLite-based memory store with vector search (sqlite-vec) and full-text search (FTS5).
/// Supports both SDK embedded and MCP server standalone scenarios.
/// </summary>
public sealed class SqliteVecMemoryStore : IMemoryStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly int _vectorDimensions;
    private readonly SqliteOptions _options;
    private readonly ILogger<SqliteVecMemoryStore> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SqliteConnection? _connection;
    private bool _initialized;

    private const string TableName = "memories";
    private const string VectorTableName = "memory_vectors";
    private const string FtsTableName = "memories_fts";

    public SqliteVecMemoryStore(
        string databasePath,
        int vectorDimensions = 1024,
        SqliteOptions? options = null,
        ILogger<SqliteVecMemoryStore>? logger = null)
    {
        _options = options ?? new SqliteOptions { DatabasePath = databasePath };
        _connectionString = BuildConnectionString(_options.DatabasePath);
        _vectorDimensions = vectorDimensions;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteVecMemoryStore>.Instance;
    }

    private static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);

            // Configure SQLite settings
            await ConfigureSqliteAsync(cancellationToken);

            // Create schema
            await CreateSchemaAsync(cancellationToken);

            _initialized = true;
            _logger.LogInformation("Initialized SQLite memory store at {DatabasePath}", _options.DatabasePath);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task ConfigureSqliteAsync(CancellationToken cancellationToken)
    {
        if (_connection == null) return;

        // Set busy timeout
        await ExecuteNonQueryAsync($"PRAGMA busy_timeout = {_options.BusyTimeoutMs};", cancellationToken);

        // Enable WAL mode for better concurrency
        if (_options.UseWalMode)
        {
            await ExecuteNonQueryAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        }

        // Set cache size
        await ExecuteNonQueryAsync($"PRAGMA cache_size = -{_options.CacheSizeKb};", cancellationToken);

        // Enable foreign keys
        await ExecuteNonQueryAsync("PRAGMA foreign_keys = ON;", cancellationToken);

        // Optimize for performance
        await ExecuteNonQueryAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA temp_store = MEMORY;", cancellationToken);

        _logger.LogDebug("SQLite configured with WAL={WalMode}, CacheSize={CacheKb}KB",
            _options.UseWalMode, _options.CacheSizeKb);
    }

    private async Task CreateSchemaAsync(CancellationToken cancellationToken)
    {
        // Main memories table
        var createTableSql = $@"
            CREATE TABLE IF NOT EXISTS {TableName} (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                session_id TEXT,
                content TEXT NOT NULL,
                content_hash TEXT,
                type INTEGER NOT NULL DEFAULT 0,
                importance_score REAL DEFAULT 0.5,
                access_count INTEGER DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_accessed_at TEXT,
                is_deleted INTEGER DEFAULT 0,
                topics TEXT,
                entities TEXT,
                metadata TEXT,
                embedding BLOB
            );

            CREATE INDEX IF NOT EXISTS idx_{TableName}_user_id ON {TableName}(user_id);
            CREATE INDEX IF NOT EXISTS idx_{TableName}_session_id ON {TableName}(session_id);
            CREATE INDEX IF NOT EXISTS idx_{TableName}_created_at ON {TableName}(created_at);
            CREATE INDEX IF NOT EXISTS idx_{TableName}_type ON {TableName}(type);
            CREATE INDEX IF NOT EXISTS idx_{TableName}_is_deleted ON {TableName}(is_deleted);
            CREATE INDEX IF NOT EXISTS idx_{TableName}_importance ON {TableName}(importance_score);
        ";

        await ExecuteNonQueryAsync(createTableSql, cancellationToken);

        // Create FTS5 virtual table if enabled
        if (_options.EnableFullTextSearch)
        {
            await CreateFtsTableAsync(cancellationToken);
        }

        _logger.LogDebug("Schema created with FTS={FtsEnabled}, Vector={VectorEnabled}",
            _options.EnableFullTextSearch, _options.EnableVectorSearch);
    }

    private async Task CreateFtsTableAsync(CancellationToken cancellationToken)
    {
        // Check if FTS table already exists
        var checkSql = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{FtsTableName}'";
        var exists = await ExecuteScalarAsync<string>(checkSql, cancellationToken);

        if (exists != null) return;

        var tokenizer = _options.FtsTokenizer switch
        {
            "porter" => "porter unicode61",
            "unicode61" => "unicode61",
            _ => "trigram"
        };

        var createFtsSql = $@"
            CREATE VIRTUAL TABLE IF NOT EXISTS {FtsTableName} USING fts5(
                content,
                topics,
                entities,
                content={TableName},
                content_rowid=rowid,
                tokenize='{tokenizer}'
            );
        ";

        await ExecuteNonQueryAsync(createFtsSql, cancellationToken);

        // Create triggers to keep FTS in sync
        var triggersSql = $@"
            CREATE TRIGGER IF NOT EXISTS {TableName}_ai AFTER INSERT ON {TableName} BEGIN
                INSERT INTO {FtsTableName}(rowid, content, topics, entities)
                VALUES (NEW.rowid, NEW.content, NEW.topics, NEW.entities);
            END;

            CREATE TRIGGER IF NOT EXISTS {TableName}_ad AFTER DELETE ON {TableName} BEGIN
                INSERT INTO {FtsTableName}({FtsTableName}, rowid, content, topics, entities)
                VALUES ('delete', OLD.rowid, OLD.content, OLD.topics, OLD.entities);
            END;

            CREATE TRIGGER IF NOT EXISTS {TableName}_au AFTER UPDATE ON {TableName} BEGIN
                INSERT INTO {FtsTableName}({FtsTableName}, rowid, content, topics, entities)
                VALUES ('delete', OLD.rowid, OLD.content, OLD.topics, OLD.entities);
                INSERT INTO {FtsTableName}(rowid, content, topics, entities)
                VALUES (NEW.rowid, NEW.content, NEW.topics, NEW.entities);
            END;
        ";

        await ExecuteNonQueryAsync(triggersSql, cancellationToken);

        _logger.LogDebug("FTS5 table created with tokenizer: {Tokenizer}", tokenizer);
    }

    /// <inheritdoc />
    public async Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        await ExecuteNonQueryAsync($"DROP TABLE IF EXISTS {FtsTableName};", cancellationToken);
        await ExecuteNonQueryAsync($"DROP TABLE IF EXISTS {TableName};", cancellationToken);

        _initialized = false;
        _logger.LogInformation("Deleted SQLite collection");
    }

    /// <inheritdoc />
    public async Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        // Generate new ID if empty
        if (memory.Id == Guid.Empty)
        {
            memory.Id = Guid.NewGuid();
        }

        var sql = $@"
            INSERT OR REPLACE INTO {TableName} (
                id, user_id, session_id, content, content_hash, type,
                importance_score, access_count, created_at, updated_at,
                last_accessed_at, is_deleted, topics, entities, metadata, embedding
            ) VALUES (
                @id, @user_id, @session_id, @content, @content_hash, @type,
                @importance_score, @access_count, @created_at, @updated_at,
                @last_accessed_at, @is_deleted, @topics, @entities, @metadata, @embedding
            )
        ";

        using var command = CreateCommand(sql);
        AddMemoryParameters(command, memory);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Stored memory {MemoryId}", memory.Id);
        return memory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(
        IEnumerable<MemoryUnit> memories,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var memoryList = memories.ToList();
        if (memoryList.Count == 0) return memoryList;

        using var transaction = _connection!.BeginTransaction();
        try
        {
            foreach (var memory in memoryList)
            {
                var sql = $@"
                    INSERT OR REPLACE INTO {TableName} (
                        id, user_id, session_id, content, content_hash, type,
                        importance_score, access_count, created_at, updated_at,
                        last_accessed_at, is_deleted, topics, entities, metadata, embedding
                    ) VALUES (
                        @id, @user_id, @session_id, @content, @content_hash, @type,
                        @importance_score, @access_count, @created_at, @updated_at,
                        @last_accessed_at, @is_deleted, @topics, @entities, @metadata, @embedding
                    )
                ";

                using var command = CreateCommand(sql);
                command.Transaction = transaction;
                AddMemoryParameters(command, memory);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Stored {Count} memories in batch", memoryList.Count);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return memoryList;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var sql = $"SELECT * FROM {TableName} WHERE id = @id";
        using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadMemoryFromReader(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var idList = ids.ToList();
        if (idList.Count == 0) return [];

        var placeholders = string.Join(", ", idList.Select((_, i) => $"@id{i}"));
        var sql = $"SELECT * FROM {TableName} WHERE id IN ({placeholders})";

        using var command = CreateCommand(sql);
        for (int i = 0; i < idList.Count; i++)
        {
            command.Parameters.AddWithValue($"@id{i}", idList[i].ToString());
        }

        var results = new List<MemoryUnit>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadMemoryFromReader(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GetAllAsync(
        string userId,
        MemoryFilterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var sql = BuildFilterQuery(userId, options);
        using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("@user_id", userId);

        if (!string.IsNullOrEmpty(options?.SessionId))
        {
            command.Parameters.AddWithValue("@session_id", options.SessionId);
        }

        var results = new List<MemoryUnit>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadMemoryFromReader(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        // Get all candidate memories with filters
        var filterSql = BuildSearchFilterQuery(options);
        using var command = CreateCommand(filterSql);

        if (!string.IsNullOrEmpty(options.UserId))
        {
            command.Parameters.AddWithValue("@user_id", options.UserId);
        }
        if (!string.IsNullOrEmpty(options.SessionId))
        {
            command.Parameters.AddWithValue("@session_id", options.SessionId);
        }

        var candidates = new List<MemoryUnit>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(ReadMemoryFromReader(reader));
            }
        }

        // Calculate cosine similarity for each candidate
        var queryVector = queryEmbedding.ToArray();
        var results = candidates
            .Where(m => m.Embedding.HasValue)
            .Select(m => new MemorySearchResult
            {
                Memory = m,
                Score = CosineSimilarity(queryVector, m.Embedding!.Value.ToArray())
            })
            .Where(r => r.Score >= options.MinScore)
            .OrderByDescending(r => r.Score)
            .Take(options.Limit)
            .ToList();

        _logger.LogDebug("Vector search found {Count} results from {Total} candidates",
            results.Count, candidates.Count);

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        memory.MarkUpdated();

        var sql = $@"
            UPDATE {TableName} SET
                user_id = @user_id,
                session_id = @session_id,
                content = @content,
                content_hash = @content_hash,
                type = @type,
                importance_score = @importance_score,
                access_count = @access_count,
                updated_at = @updated_at,
                last_accessed_at = @last_accessed_at,
                is_deleted = @is_deleted,
                topics = @topics,
                entities = @entities,
                metadata = @metadata,
                embedding = @embedding
            WHERE id = @id
        ";

        using var command = CreateCommand(sql);
        AddMemoryParameters(command, memory);
        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogDebug("Updated memory {MemoryId}", memory.Id);
        }

        return rowsAffected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        string sql;
        if (hardDelete)
        {
            sql = $"DELETE FROM {TableName} WHERE id = @id";
        }
        else
        {
            sql = $"UPDATE {TableName} SET is_deleted = 1, updated_at = @updated_at WHERE id = @id";
        }

        using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("@id", id.ToString());
        if (!hardDelete)
        {
            command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("O"));
        }

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogDebug("{DeleteType} deleted memory {MemoryId}",
                hardDelete ? "Hard" : "Soft", id);
        }

        return rowsAffected > 0;
    }

    /// <inheritdoc />
    public async Task<long> GetCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var sql = $"SELECT COUNT(*) FROM {TableName} WHERE user_id = @user_id AND is_deleted = 0";
        using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("@user_id", userId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Performs full-text search using FTS5 with BM25 ranking.
    /// </summary>
    public async Task<IReadOnlyList<MemorySearchResult>> FullTextSearchAsync(
        string query,
        string? userId = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        if (!_options.EnableFullTextSearch)
        {
            _logger.LogWarning("Full-text search is disabled");
            return [];
        }

        var sql = $@"
            SELECT m.*, bm25({FtsTableName}) as score
            FROM {TableName} m
            INNER JOIN {FtsTableName} fts ON m.rowid = fts.rowid
            WHERE {FtsTableName} MATCH @query
            AND m.is_deleted = 0
            {(userId != null ? "AND m.user_id = @user_id" : "")}
            ORDER BY score
            LIMIT @limit
        ";

        using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("@query", query);
        command.Parameters.AddWithValue("@limit", limit);
        if (userId != null)
        {
            command.Parameters.AddWithValue("@user_id", userId);
        }

        var results = new List<MemorySearchResult>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var memory = ReadMemoryFromReader(reader);
            var score = reader.GetFloat(reader.GetOrdinal("score"));
            // BM25 returns negative scores (lower is better), normalize to 0-1 range
            var normalizedScore = 1.0f / (1.0f + Math.Abs(score));

            results.Add(new MemorySearchResult
            {
                Memory = memory,
                Score = normalizedScore
            });
        }

        _logger.LogDebug("FTS search for '{Query}' found {Count} results", query, results.Count);
        return results;
    }

    /// <summary>
    /// Performs hybrid search combining vector similarity and BM25 full-text search.
    /// Uses Reciprocal Rank Fusion (RRF) for score combination.
    /// </summary>
    public async Task<IReadOnlyList<MemorySearchResult>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> queryEmbedding,
        string? userId = null,
        int limit = 10,
        float denseWeight = 0.6f,
        float sparseWeight = 0.4f,
        int rrfK = 60,
        CancellationToken cancellationToken = default)
    {
        // Get vector search results
        var vectorOptions = new MemorySearchOptions
        {
            UserId = userId,
            Limit = limit * 2, // Get more candidates for fusion
            MinScore = 0.0f
        };
        var vectorResults = await SearchAsync(queryEmbedding, vectorOptions, cancellationToken);

        // Get FTS results
        var ftsResults = await FullTextSearchAsync(query, userId, limit * 2, cancellationToken);

        // Apply RRF fusion
        var fusedScores = new Dictionary<Guid, float>();

        // Add vector results with RRF score
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var id = vectorResults[i].Memory.Id;
            var rrfScore = denseWeight / (rrfK + i + 1);
            fusedScores[id] = fusedScores.GetValueOrDefault(id, 0) + rrfScore;
        }

        // Add FTS results with RRF score
        for (int i = 0; i < ftsResults.Count; i++)
        {
            var id = ftsResults[i].Memory.Id;
            var rrfScore = sparseWeight / (rrfK + i + 1);
            fusedScores[id] = fusedScores.GetValueOrDefault(id, 0) + rrfScore;
        }

        // Merge results and sort by fused score
        var allMemories = vectorResults.Concat(ftsResults)
            .GroupBy(r => r.Memory.Id)
            .Select(g => g.First().Memory)
            .ToDictionary(m => m.Id);

        var results = fusedScores
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new MemorySearchResult
            {
                Memory = allMemories[kv.Key],
                Score = kv.Value
            })
            .ToList();

        _logger.LogDebug("Hybrid search found {Count} results (dense={DenseCount}, sparse={SparseCount})",
            results.Count, vectorResults.Count, ftsResults.Count);

        return results;
    }

    /// <summary>
    /// Optimizes the database by running VACUUM and ANALYZE.
    /// </summary>
    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        await ExecuteNonQueryAsync("ANALYZE;", cancellationToken);

        if (_options.EnableFullTextSearch)
        {
            await ExecuteNonQueryAsync($"INSERT INTO {FtsTableName}({FtsTableName}) VALUES('optimize');", cancellationToken);
        }

        _logger.LogInformation("Database optimized");
    }

    /// <summary>
    /// Checkpoints the WAL file for better read performance.
    /// </summary>
    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.UseWalMode) return;

        await EnsureCollectionExistsAsync(cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
        _logger.LogDebug("WAL checkpoint completed");
    }

    #region Helper Methods

    private SqliteCommand CreateCommand(string sql)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Database connection not initialized");
        }
        return new SqliteCommand(sql, _connection);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        using var command = CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T?> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        using var command = CreateCommand(sql);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? default : (T)result;
    }

    private static void AddMemoryParameters(SqliteCommand command, MemoryUnit memory)
    {
        command.Parameters.AddWithValue("@id", memory.Id.ToString());
        command.Parameters.AddWithValue("@user_id", memory.UserId ?? string.Empty);
        command.Parameters.AddWithValue("@session_id", (object?)memory.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@content", memory.Content);
        command.Parameters.AddWithValue("@content_hash", (object?)memory.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("@type", (int)memory.Type);
        command.Parameters.AddWithValue("@importance_score", memory.ImportanceScore);
        command.Parameters.AddWithValue("@access_count", memory.AccessCount);
        command.Parameters.AddWithValue("@created_at", memory.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@updated_at", memory.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("@last_accessed_at",
            memory.LastAccessedAt.HasValue ? memory.LastAccessedAt.Value.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("@is_deleted", memory.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("@topics", JsonSerializer.Serialize(memory.Topics));
        command.Parameters.AddWithValue("@entities", JsonSerializer.Serialize(memory.Entities));
        command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(memory.Metadata));

        // Store embedding as blob
        if (memory.Embedding.HasValue)
        {
            var embeddingBytes = new byte[memory.Embedding.Value.Length * sizeof(float)];
            Buffer.BlockCopy(memory.Embedding.Value.ToArray(), 0, embeddingBytes, 0, embeddingBytes.Length);
            command.Parameters.AddWithValue("@embedding", embeddingBytes);
        }
        else
        {
            command.Parameters.AddWithValue("@embedding", DBNull.Value);
        }
    }

    private static MemoryUnit ReadMemoryFromReader(SqliteDataReader reader)
    {
        var memory = new MemoryUnit
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            SessionId = reader.IsDBNull(reader.GetOrdinal("session_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("session_id")),
            Content = reader.GetString(reader.GetOrdinal("content")),
            ContentHash = reader.IsDBNull(reader.GetOrdinal("content_hash"))
                ? null
                : reader.GetString(reader.GetOrdinal("content_hash")),
            Type = (MemoryType)reader.GetInt32(reader.GetOrdinal("type")),
            ImportanceScore = reader.GetFloat(reader.GetOrdinal("importance_score")),
            AccessCount = reader.GetInt32(reader.GetOrdinal("access_count")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
            IsDeleted = reader.GetInt32(reader.GetOrdinal("is_deleted")) == 1
        };

        // Parse optional datetime
        var lastAccessedOrdinal = reader.GetOrdinal("last_accessed_at");
        if (!reader.IsDBNull(lastAccessedOrdinal))
        {
            var lastAccessedStr = reader.GetString(lastAccessedOrdinal);
            if (!string.IsNullOrEmpty(lastAccessedStr))
            {
                memory.LastAccessedAt = DateTime.Parse(lastAccessedStr);
            }
        }

        // Parse JSON arrays
        var topicsOrdinal = reader.GetOrdinal("topics");
        if (!reader.IsDBNull(topicsOrdinal))
        {
            var topicsJson = reader.GetString(topicsOrdinal);
            memory.Topics = JsonSerializer.Deserialize<List<string>>(topicsJson) ?? [];
        }

        var entitiesOrdinal = reader.GetOrdinal("entities");
        if (!reader.IsDBNull(entitiesOrdinal))
        {
            var entitiesJson = reader.GetString(entitiesOrdinal);
            memory.Entities = JsonSerializer.Deserialize<List<string>>(entitiesJson) ?? [];
        }

        var metadataOrdinal = reader.GetOrdinal("metadata");
        if (!reader.IsDBNull(metadataOrdinal))
        {
            var metadataJson = reader.GetString(metadataOrdinal);
            memory.Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? [];
        }

        // Parse embedding blob
        var embeddingOrdinal = reader.GetOrdinal("embedding");
        if (!reader.IsDBNull(embeddingOrdinal))
        {
            var embeddingBytes = (byte[])reader.GetValue(embeddingOrdinal);
            var floatCount = embeddingBytes.Length / sizeof(float);
            var floats = new float[floatCount];
            Buffer.BlockCopy(embeddingBytes, 0, floats, 0, embeddingBytes.Length);
            memory.Embedding = floats;
        }

        return memory;
    }

    private static string BuildFilterQuery(string userId, MemoryFilterOptions? options)
    {
        var conditions = new List<string> { "user_id = @user_id" };

        if (options?.IncludeDeleted != true)
        {
            conditions.Add("is_deleted = 0");
        }

        if (!string.IsNullOrEmpty(options?.SessionId))
        {
            conditions.Add("session_id = @session_id");
        }

        if (options?.Types?.Length > 0)
        {
            var typeConditions = string.Join(" OR ", options.Types.Select(t => $"type = {(int)t}"));
            conditions.Add($"({typeConditions})");
        }

        var orderBy = "ORDER BY created_at DESC";
        var limit = options?.Limit > 0 ? $"LIMIT {options.Limit}" : "";

        return $"SELECT * FROM {TableName} WHERE {string.Join(" AND ", conditions)} {orderBy} {limit}";
    }

    private static string BuildSearchFilterQuery(MemorySearchOptions options)
    {
        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(options.UserId))
        {
            conditions.Add("user_id = @user_id");
        }

        if (!string.IsNullOrEmpty(options.SessionId))
        {
            conditions.Add("session_id = @session_id");
        }

        if (!options.IncludeDeleted)
        {
            conditions.Add("is_deleted = 0");
        }

        if (options.Types?.Length > 0)
        {
            var typeConditions = string.Join(" OR ", options.Types.Select(t => $"type = {(int)t}"));
            conditions.Add($"({typeConditions})");
        }

        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
        return $"SELECT * FROM {TableName} {whereClause}";
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator == 0 ? 0 : (float)(dotProduct / denominator);
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }

        _initLock.Dispose();
    }
}
