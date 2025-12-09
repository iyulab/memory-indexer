namespace MemoryIndexer.Intelligence.SelfEditing;

/// <summary>
/// Service for self-editing memory operations following MemGPT patterns.
/// Enables LLMs to manage their own context through structured memory operations.
/// </summary>
public interface ISelfEditingMemoryService
{
    /// <summary>
    /// Replaces content in working memory at specified location.
    /// </summary>
    /// <param name="location">The memory location to update.</param>
    /// <param name="newContent">The new content to place at location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the replace operation.</returns>
    Task<MemoryOperationResult> ReplaceWorkingMemoryAsync(
        string location,
        string newContent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts content into archival memory for long-term storage.
    /// </summary>
    /// <param name="content">Content to archive.</param>
    /// <param name="metadata">Optional metadata about the content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the archived memory ID.</returns>
    Task<ArchivalInsertResult> InsertArchivalMemoryAsync(
        string content,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches archival memory for relevant content.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching archival memories.</returns>
    Task<IReadOnlyList<ArchivalSearchResult>> SearchArchivalMemoryAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current working memory state.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current working memory snapshot.</returns>
    Task<WorkingMemorySnapshot> GetWorkingMemoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates working memory with new context, potentially triggering reflection.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="newContext">New context to incorporate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update result with potential reflection trigger.</returns>
    Task<WorkingMemoryUpdateResult> UpdateWorkingMemoryAsync(
        string sessionId,
        string newContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if reflection should be triggered based on importance accumulation.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if reflection is recommended.</returns>
    Task<ReflectionCheckResult> ShouldTriggerReflectionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a reflection operation to consolidate memories.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the reflection process.</returns>
    Task<ReflectionResult> PerformReflectionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manages the context window by pruning or archiving old content.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="maxTokens">Maximum tokens to retain in working memory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the management operation.</returns>
    Task<ContextManagementResult> ManageContextWindowAsync(
        string sessionId,
        int maxTokens,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a memory operation.
/// </summary>
public sealed class MemoryOperationResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Previous content that was replaced (if applicable).
    /// </summary>
    public string? PreviousContent { get; set; }

    /// <summary>
    /// Timestamp of the operation.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of archival insert operation.
/// </summary>
public sealed class ArchivalInsertResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// ID of the archived memory.
    /// </summary>
    public Guid MemoryId { get; set; }

    /// <summary>
    /// Token count of archived content.
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Timestamp of archival.
    /// </summary>
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result from archival memory search.
/// </summary>
public sealed class ArchivalSearchResult
{
    /// <summary>
    /// Memory ID.
    /// </summary>
    public Guid MemoryId { get; set; }

    /// <summary>
    /// Content of the memory.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Relevance score to query.
    /// </summary>
    public float RelevanceScore { get; set; }

    /// <summary>
    /// When this memory was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Associated metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>
/// Snapshot of working memory state.
/// </summary>
public sealed class WorkingMemorySnapshot
{
    /// <summary>
    /// Session ID this snapshot belongs to.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Core persona/identity information.
    /// </summary>
    public string CoreMemory { get; set; } = string.Empty;

    /// <summary>
    /// Current conversation context.
    /// </summary>
    public string ConversationContext { get; set; } = string.Empty;

    /// <summary>
    /// Recent interaction summaries.
    /// </summary>
    public List<string> RecentSummaries { get; set; } = [];

    /// <summary>
    /// Current token usage.
    /// </summary>
    public int CurrentTokenCount { get; set; }

    /// <summary>
    /// Maximum token capacity.
    /// </summary>
    public int MaxTokenCapacity { get; set; }

    /// <summary>
    /// Accumulated importance since last reflection.
    /// </summary>
    public float AccumulatedImportance { get; set; }

    /// <summary>
    /// Time of last update.
    /// </summary>
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Result of working memory update.
/// </summary>
public sealed class WorkingMemoryUpdateResult
{
    /// <summary>
    /// Whether update succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Whether context was truncated.
    /// </summary>
    public bool WasTruncated { get; set; }

    /// <summary>
    /// Content that was archived due to truncation.
    /// </summary>
    public string? ArchivedContent { get; set; }

    /// <summary>
    /// Whether reflection is recommended.
    /// </summary>
    public bool ReflectionRecommended { get; set; }

    /// <summary>
    /// New token count after update.
    /// </summary>
    public int NewTokenCount { get; set; }
}

/// <summary>
/// Result of reflection check.
/// </summary>
public sealed class ReflectionCheckResult
{
    /// <summary>
    /// Whether reflection should be triggered.
    /// </summary>
    public bool ShouldReflect { get; set; }

    /// <summary>
    /// Current accumulated importance score.
    /// </summary>
    public float AccumulatedImportance { get; set; }

    /// <summary>
    /// Threshold for triggering reflection.
    /// </summary>
    public float Threshold { get; set; }

    /// <summary>
    /// Reason for recommendation.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result of reflection process.
/// </summary>
public sealed class ReflectionResult
{
    /// <summary>
    /// Whether reflection succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of memories consolidated.
    /// </summary>
    public int MemoriesConsolidated { get; set; }

    /// <summary>
    /// Generated insights from reflection.
    /// </summary>
    public List<string> Insights { get; set; } = [];

    /// <summary>
    /// Number of memories archived.
    /// </summary>
    public int MemoriesArchived { get; set; }

    /// <summary>
    /// Tokens freed by reflection.
    /// </summary>
    public int TokensFreed { get; set; }
}

/// <summary>
/// Result of context window management.
/// </summary>
public sealed class ContextManagementResult
{
    /// <summary>
    /// Whether management succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Action taken (none, pruned, archived, summarized).
    /// </summary>
    public ContextManagementAction ActionTaken { get; set; }

    /// <summary>
    /// Tokens before management.
    /// </summary>
    public int TokensBefore { get; set; }

    /// <summary>
    /// Tokens after management.
    /// </summary>
    public int TokensAfter { get; set; }

    /// <summary>
    /// Content that was removed or archived.
    /// </summary>
    public List<string> ProcessedContent { get; set; } = [];
}

/// <summary>
/// Actions that can be taken during context management.
/// </summary>
public enum ContextManagementAction
{
    /// <summary>
    /// No action needed.
    /// </summary>
    None,

    /// <summary>
    /// Old content was pruned/removed.
    /// </summary>
    Pruned,

    /// <summary>
    /// Content was archived to long-term storage.
    /// </summary>
    Archived,

    /// <summary>
    /// Content was summarized to reduce size.
    /// </summary>
    Summarized,

    /// <summary>
    /// Multiple actions were taken.
    /// </summary>
    Combined
}
