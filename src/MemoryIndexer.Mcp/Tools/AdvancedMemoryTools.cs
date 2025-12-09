using System.ComponentModel;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Services;
using MemoryIndexer.Intelligence.Deduplication;
using MemoryIndexer.Intelligence.Search;
using MemoryIndexer.Intelligence.Scoring;
using MemoryIndexer.Intelligence.Summarization;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Mcp.Tools;

/// <summary>
/// Advanced MCP tools for enhanced memory operations.
/// Includes hybrid search, duplicate detection, and memory merging.
/// </summary>
[McpServerToolType]
public sealed class AdvancedMemoryTools
{
    private readonly MemoryService _memoryService;
    private readonly IHybridSearchService _hybridSearch;
    private readonly DuplicateDetector _duplicateDetector;
    private readonly ImportanceAnalyzer _importanceAnalyzer;
    private readonly ISummarizationService _summarizationService;

    private const string DefaultUserId = "default";

    public AdvancedMemoryTools(
        MemoryService memoryService,
        IHybridSearchService hybridSearch,
        DuplicateDetector duplicateDetector,
        ImportanceAnalyzer importanceAnalyzer,
        ISummarizationService summarizationService)
    {
        _memoryService = memoryService;
        _hybridSearch = hybridSearch;
        _duplicateDetector = duplicateDetector;
        _importanceAnalyzer = importanceAnalyzer;
        _summarizationService = summarizationService;
    }

    /// <summary>
    /// Advanced search using hybrid retrieval (semantic + keyword).
    /// Better for queries containing specific terms or mixed requirements.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <param name="denseWeight">Weight for semantic search (0.0 to 1.0).</param>
    /// <param name="sparseWeight">Weight for keyword search (0.0 to 1.0).</param>
    /// <param name="useDiversity">Apply diversity filtering to avoid redundant results.</param>
    /// <param name="type">Filter by memory type.</param>
    /// <param name="sessionId">Filter by session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with relevance scores.</returns>
    [McpServerTool]
    [Description("Advanced hybrid search combining semantic understanding and keyword matching. Best for queries with specific terms or technical content.")]
    public async Task<SearchMemoryResult> SearchMemory(
        [Description("Search query")] string query,
        [Description("Maximum results (1-20)")] int limit = 5,
        [Description("Semantic search weight (0.0-1.0)")] float denseWeight = 0.6f,
        [Description("Keyword search weight (0.0-1.0)")] float sparseWeight = 0.4f,
        [Description("Apply diversity filtering")] bool useDiversity = false,
        [Description("Filter by memory type")] string? type = null,
        [Description("Filter by session ID")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var options = new HybridSearchOptions
        {
            UserId = DefaultUserId,
            SessionId = sessionId,
            Limit = Math.Clamp(limit, 1, 20),
            DenseWeight = Math.Clamp(denseWeight, 0f, 1f),
            SparseWeight = Math.Clamp(sparseWeight, 0f, 1f),
            UseMmr = useDiversity,
            MmrLambda = 0.7f
        };

        if (!string.IsNullOrWhiteSpace(type))
        {
            options.Types = [ParseMemoryType(type)];
        }

        var results = await _hybridSearch.SearchAsync(query, options, cancellationToken);

        return new SearchMemoryResult
        {
            Success = true,
            Count = results.Count,
            Memories = results.Select(r => new HybridMemoryItem
            {
                Id = r.Memory.Id.ToString(),
                Content = r.Memory.Content,
                Type = r.Memory.Type.ToString().ToLowerInvariant(),
                Score = r.Score,
                DenseScore = r.DenseScore,
                SparseScore = r.SparseScore,
                SearchType = r.SearchType.ToString().ToLowerInvariant(),
                Importance = r.Memory.ImportanceScore,
                CreatedAt = r.Memory.CreatedAt,
                AccessCount = r.Memory.AccessCount
            }).ToList()
        };
    }

    /// <summary>
    /// Finds memories related to a specific memory by semantic similarity.
    /// </summary>
    /// <param name="memoryId">The ID of the memory to find relations for.</param>
    /// <param name="limit">Maximum related memories to return.</param>
    /// <param name="minSimilarity">Minimum similarity threshold (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of related memories.</returns>
    [McpServerTool]
    [Description("Find memories semantically related to a specific memory. Useful for discovering connections and context.")]
    public async Task<GetRelatedMemoriesResult> GetRelatedMemories(
        [Description("Memory ID to find relations for")] string memoryId,
        [Description("Maximum related memories")] int limit = 5,
        [Description("Minimum similarity (0.0-1.0)")] float minSimilarity = 0.5f,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(memoryId, out var id))
        {
            return new GetRelatedMemoriesResult
            {
                Success = false,
                Message = "Invalid memory ID format"
            };
        }

        var memory = await _memoryService.GetByIdAsync(id, cancellationToken);
        if (memory is null)
        {
            return new GetRelatedMemoriesResult
            {
                Success = false,
                Message = "Memory not found"
            };
        }

        // Search using the memory's content
        var results = await _hybridSearch.SearchAsync(
            memory.Content,
            new HybridSearchOptions
            {
                UserId = DefaultUserId,
                Limit = limit + 1, // +1 to exclude self
                MinScore = minSimilarity
            },
            cancellationToken);

        // Exclude the source memory
        var related = results
            .Where(r => r.Memory.Id != id)
            .Take(limit)
            .ToList();

        return new GetRelatedMemoriesResult
        {
            Success = true,
            SourceMemoryId = memoryId,
            Count = related.Count,
            RelatedMemories = related.Select(r => new RelatedMemoryItem
            {
                Id = r.Memory.Id.ToString(),
                Content = r.Memory.Content,
                Type = r.Memory.Type.ToString().ToLowerInvariant(),
                Similarity = r.Score,
                Importance = r.Memory.ImportanceScore,
                CreatedAt = r.Memory.CreatedAt
            }).ToList()
        };
    }

    /// <summary>
    /// Checks if content is a duplicate of existing memories.
    /// </summary>
    /// <param name="content">Content to check for duplicates.</param>
    /// <param name="threshold">Similarity threshold (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Duplicate check result with recommendations.</returns>
    [McpServerTool]
    [Description("Check if content is a duplicate of existing memories. Helps avoid storing redundant information.")]
    public async Task<CheckDuplicateResult> CheckDuplicate(
        [Description("Content to check for duplicates")] string content,
        [Description("Similarity threshold (0.0-1.0)")] float threshold = 0.8f,
        CancellationToken cancellationToken = default)
    {
        var result = await _duplicateDetector.CheckForDuplicateAsync(
            content,
            DefaultUserId,
            Math.Clamp(threshold, 0.5f, 0.99f),
            cancellationToken);

        return new CheckDuplicateResult
        {
            IsDuplicate = result.IsDuplicate,
            DuplicateType = result.DuplicateType.ToString().ToLowerInvariant(),
            SimilarityScore = result.SimilarityScore,
            RecommendedAction = result.RecommendedAction.ToString().ToLowerInvariant(),
            ExistingMemoryId = result.ExistingMemory?.Id.ToString(),
            ExistingContent = result.ExistingMemory?.Content,
            SimilarCount = result.SimilarMemories.Count
        };
    }

    /// <summary>
    /// Finds and optionally merges duplicate memories.
    /// </summary>
    /// <param name="autoMerge">Automatically merge found duplicates.</param>
    /// <param name="threshold">Similarity threshold for duplicate detection.</param>
    /// <param name="mergeStrategy">Strategy for merging: keepOldest, keepNewest, keepMostAccessed, keepHighestImportance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Duplicate detection and merge result.</returns>
    [McpServerTool]
    [Description("Find duplicate memories and optionally merge them. Helps clean up redundant stored information.")]
    public async Task<MergeMemoriesResult> FindAndMergeDuplicates(
        [Description("Automatically merge duplicates")] bool autoMerge = false,
        [Description("Similarity threshold (0.0-1.0)")] float threshold = 0.8f,
        [Description("Merge strategy: keepOldest, keepNewest, keepMostAccessed, keepHighestImportance")] string mergeStrategy = "keepOldest",
        CancellationToken cancellationToken = default)
    {
        var groups = await _duplicateDetector.FindAllDuplicatesAsync(
            DefaultUserId,
            Math.Clamp(threshold, 0.5f, 0.99f),
            cancellationToken);

        var result = new MergeMemoriesResult
        {
            Success = true,
            DuplicateGroupsFound = groups.Count,
            TotalDuplicates = groups.Sum(g => g.Duplicates.Count),
            MergedCount = 0,
            Groups = groups.Select(g => new DuplicateGroupInfo
            {
                PrimaryMemoryId = g.PrimaryMemory.Id.ToString(),
                PrimaryContent = g.PrimaryMemory.Content.Length > 100
                    ? g.PrimaryMemory.Content[..100] + "..."
                    : g.PrimaryMemory.Content,
                DuplicateCount = g.Duplicates.Count,
                DuplicateIds = g.Duplicates.Select(d => d.Id.ToString()).ToList()
            }).ToList()
        };

        if (autoMerge && groups.Count > 0)
        {
            var strategy = ParseMergeStrategy(mergeStrategy);

            foreach (var group in groups)
            {
                await _duplicateDetector.MergeDuplicatesAsync(group, strategy, cancellationToken);
                result.MergedCount++;
            }

            result.Message = $"Merged {result.MergedCount} duplicate groups";
        }
        else if (groups.Count > 0)
        {
            result.Message = $"Found {groups.Count} duplicate groups. Set autoMerge=true to merge them.";
        }
        else
        {
            result.Message = "No duplicate memories found";
        }

        return result;
    }

    /// <summary>
    /// Analyzes content and suggests an importance score.
    /// </summary>
    /// <param name="content">Content to analyze.</param>
    /// <param name="type">Memory type for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Suggested importance score with analysis.</returns>
    [McpServerTool]
    [Description("Analyze content to suggest an appropriate importance score before storing.")]
    public Task<AnalyzeImportanceResult> AnalyzeImportance(
        [Description("Content to analyze")] string content,
        [Description("Memory type for context")] string type = "episodic",
        CancellationToken cancellationToken = default)
    {
        var memoryType = ParseMemoryType(type);
        var score = _importanceAnalyzer.AnalyzeImportance(content, memoryType);

        var analysis = score switch
        {
            >= 0.8f => "High importance - contains critical information, decisions, or credentials",
            >= 0.6f => "Moderate-high importance - contains notable facts, preferences, or structured information",
            >= 0.4f => "Moderate importance - contains useful context or general information",
            >= 0.2f => "Low importance - contains routine or common information",
            _ => "Minimal importance - contains generic or trivial content"
        };

        return Task.FromResult(new AnalyzeImportanceResult
        {
            Success = true,
            SuggestedImportance = score,
            Analysis = analysis,
            ContentLength = content.Length,
            MemoryType = type
        });
    }

    /// <summary>
    /// Rebuilds the search index for improved search performance.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Index rebuild result.</returns>
    [McpServerTool]
    [Description("Rebuild the search index for improved hybrid search performance. Run after bulk operations.")]
    public async Task<RebuildIndexResult> RebuildSearchIndex(
        CancellationToken cancellationToken = default)
    {
        await _hybridSearch.RebuildIndexAsync(DefaultUserId, cancellationToken);

        return new RebuildIndexResult
        {
            Success = true,
            Message = "Search index rebuilt successfully"
        };
    }

    /// <summary>
    /// Summarizes memories based on query or all memories.
    /// Creates a compressed summary for context window optimization.
    /// </summary>
    /// <param name="query">Optional query to filter memories for summarization.</param>
    /// <param name="limit">Maximum memories to summarize.</param>
    /// <param name="compressionRatio">Target compression ratio (0.1 to 0.5).</param>
    /// <param name="style">Summarization style: extractive, abstractive, hybrid.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of selected memories.</returns>
    [McpServerTool]
    [Description("Summarize memories to create a compressed representation. Useful for context window optimization.")]
    public async Task<SummarizeMemoriesResult> SummarizeMemories(
        [Description("Optional query to filter memories")] string? query = null,
        [Description("Maximum memories to summarize (1-50)")] int limit = 20,
        [Description("Target compression ratio (0.1-0.5)")] float compressionRatio = 0.3f,
        [Description("Style: extractive, abstractive, hybrid")] string style = "hybrid",
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryUnit> memories;
        var clampedLimit = Math.Clamp(limit, 1, 50);

        if (string.IsNullOrWhiteSpace(query))
        {
            var allMemories = await _memoryService.GetAllAsync(
                DefaultUserId,
                null,
                cancellationToken);
            memories = allMemories.Take(clampedLimit).ToList();
        }
        else
        {
            var searchResults = await _hybridSearch.SearchAsync(
                query,
                new HybridSearchOptions
                {
                    UserId = DefaultUserId,
                    Limit = clampedLimit
                },
                cancellationToken);
            memories = searchResults.Select(r => r.Memory).ToList();
        }

        if (memories.Count == 0)
        {
            return new SummarizeMemoriesResult
            {
                Success = false,
                Message = "No memories found to summarize"
            };
        }

        var options = new SummarizationOptions
        {
            TargetCompressionRatio = Math.Clamp(compressionRatio, 0.1f, 0.5f),
            Style = ParseSummaryStyle(style)
        };

        var summary = await _summarizationService.SummarizeAsync(memories, options, cancellationToken);

        return new SummarizeMemoriesResult
        {
            Success = true,
            SummaryId = summary.Id.ToString(),
            Content = summary.Content,
            KeyPoints = summary.KeyPoints,
            Topics = summary.Topics,
            Entities = summary.Entities,
            SourceMemoryCount = summary.SourceMemoryIds.Count,
            OriginalTokenCount = summary.OriginalTokenCount,
            SummarizedTokenCount = summary.SummarizedTokenCount,
            CompressionRatio = summary.CompressionRatio
        };
    }

    /// <summary>
    /// Creates a hierarchical summary structure from memories.
    /// Multiple levels of abstraction for efficient retrieval.
    /// </summary>
    /// <param name="levels">Number of hierarchy levels (2-5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hierarchical summary structure.</returns>
    [McpServerTool]
    [Description("Create hierarchical summary structure with multiple abstraction levels. Efficient for large memory sets.")]
    public async Task<CreateHierarchySummaryResult> CreateHierarchySummary(
        [Description("Number of hierarchy levels (2-5)")] int levels = 3,
        CancellationToken cancellationToken = default)
    {
        var allMemories = await _memoryService.GetAllAsync(
            DefaultUserId,
            null,
            cancellationToken);
        var memories = allMemories.Take(100).ToList(); // Get up to 100 memories for hierarchy

        if (memories.Count < 3)
        {
            return new CreateHierarchySummaryResult
            {
                Success = false,
                Message = "Not enough memories to create hierarchy (minimum 3 required)"
            };
        }

        var hierarchy = await _summarizationService.CreateHierarchyAsync(
            memories,
            Math.Clamp(levels, 2, 5),
            cancellationToken);

        return new CreateHierarchySummaryResult
        {
            Success = true,
            RootSummary = new SummaryLevelInfo
            {
                Id = hierarchy.RootSummary.Id.ToString(),
                Content = hierarchy.RootSummary.Content,
                KeyPoints = hierarchy.RootSummary.KeyPoints,
                SourceCount = hierarchy.RootSummary.SourceMemoryIds.Count
            },
            LevelCount = hierarchy.Levels.Count,
            TotalMemoryCount = hierarchy.TotalMemoryCount,
            OverallCompressionRatio = hierarchy.OverallCompressionRatio,
            LevelSummaries = hierarchy.Levels.Select((level, idx) => new SummaryLevelInfo
            {
                Level = idx,
                SummaryCount = level.Count,
                TotalKeyPoints = level.Sum(s => s.KeyPoints.Count)
            }).ToList()
        };
    }

    /// <summary>
    /// Checks if summarization should be triggered based on context usage.
    /// </summary>
    /// <param name="currentTokenCount">Current token usage in context.</param>
    /// <param name="maxTokens">Maximum context window size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether summarization is recommended.</returns>
    [McpServerTool]
    [Description("Check if context window optimization via summarization is recommended based on current usage.")]
    public async Task<ShouldSummarizeResult> ShouldSummarize(
        [Description("Current token count in context")] int currentTokenCount,
        [Description("Maximum tokens allowed")] int maxTokens = 128000,
        CancellationToken cancellationToken = default)
    {
        var memories = await _memoryService.GetAllAsync(DefaultUserId, null, cancellationToken);
        var memoryCount = memories.Count;

        var shouldTrigger = _summarizationService.ShouldTriggerSummarization(
            currentTokenCount,
            maxTokens,
            memoryCount);

        var usagePercentage = (float)currentTokenCount / maxTokens * 100;

        return new ShouldSummarizeResult
        {
            ShouldSummarize = shouldTrigger,
            CurrentTokenCount = currentTokenCount,
            MaxTokens = maxTokens,
            UsagePercentage = usagePercentage,
            MemoryCount = memoryCount,
            Recommendation = shouldTrigger
                ? "Summarization recommended to optimize context window usage"
                : usagePercentage > 70
                    ? "Context usage high but summarization not yet critical"
                    : "Context usage is within acceptable limits"
        };
    }

    private static MemoryType ParseMemoryType(string type) => type.ToLowerInvariant() switch
    {
        "episodic" => MemoryType.Episodic,
        "semantic" => MemoryType.Semantic,
        "procedural" => MemoryType.Procedural,
        "fact" => MemoryType.Fact,
        _ => MemoryType.Episodic
    };

    private static MergeStrategy ParseMergeStrategy(string strategy) => strategy.ToLowerInvariant() switch
    {
        "keepnewest" => MergeStrategy.KeepNewest,
        "keepmostaccessed" => MergeStrategy.KeepMostAccessed,
        "keephighestimportance" => MergeStrategy.KeepHighestImportance,
        "combinecontent" => MergeStrategy.CombineContent,
        _ => MergeStrategy.KeepOldest
    };

    private static SummaryStyle ParseSummaryStyle(string style) => style.ToLowerInvariant() switch
    {
        "extractive" => SummaryStyle.Extractive,
        "abstractive" => SummaryStyle.Abstractive,
        _ => SummaryStyle.Hybrid
    };
}

#region Result DTOs

public sealed class SearchMemoryResult
{
    public bool Success { get; set; }
    public int Count { get; set; }
    public List<HybridMemoryItem> Memories { get; set; } = [];
}

public sealed class HybridMemoryItem
{
    public string Id { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Type { get; set; } = default!;
    public float Score { get; set; }
    public float DenseScore { get; set; }
    public float SparseScore { get; set; }
    public string SearchType { get; set; } = default!;
    public float Importance { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AccessCount { get; set; }
}

public sealed class GetRelatedMemoriesResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? SourceMemoryId { get; set; }
    public int Count { get; set; }
    public List<RelatedMemoryItem> RelatedMemories { get; set; } = [];
}

public sealed class RelatedMemoryItem
{
    public string Id { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Type { get; set; } = default!;
    public float Similarity { get; set; }
    public float Importance { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CheckDuplicateResult
{
    public bool IsDuplicate { get; set; }
    public string DuplicateType { get; set; } = default!;
    public float SimilarityScore { get; set; }
    public string RecommendedAction { get; set; } = default!;
    public string? ExistingMemoryId { get; set; }
    public string? ExistingContent { get; set; }
    public int SimilarCount { get; set; }
}

public sealed class MergeMemoriesResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int DuplicateGroupsFound { get; set; }
    public int TotalDuplicates { get; set; }
    public int MergedCount { get; set; }
    public List<DuplicateGroupInfo> Groups { get; set; } = [];
}

public sealed class DuplicateGroupInfo
{
    public string PrimaryMemoryId { get; set; } = default!;
    public string PrimaryContent { get; set; } = default!;
    public int DuplicateCount { get; set; }
    public List<string> DuplicateIds { get; set; } = [];
}

public sealed class AnalyzeImportanceResult
{
    public bool Success { get; set; }
    public float SuggestedImportance { get; set; }
    public string Analysis { get; set; } = default!;
    public int ContentLength { get; set; }
    public string MemoryType { get; set; } = default!;
}

public sealed class RebuildIndexResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public sealed class SummarizeMemoriesResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? SummaryId { get; set; }
    public string? Content { get; set; }
    public List<string> KeyPoints { get; set; } = [];
    public List<string> Topics { get; set; } = [];
    public List<string> Entities { get; set; } = [];
    public int SourceMemoryCount { get; set; }
    public int OriginalTokenCount { get; set; }
    public int SummarizedTokenCount { get; set; }
    public float CompressionRatio { get; set; }
}

public sealed class CreateHierarchySummaryResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public SummaryLevelInfo? RootSummary { get; set; }
    public int LevelCount { get; set; }
    public int TotalMemoryCount { get; set; }
    public float OverallCompressionRatio { get; set; }
    public List<SummaryLevelInfo> LevelSummaries { get; set; } = [];
}

public sealed class SummaryLevelInfo
{
    public int Level { get; set; }
    public string? Id { get; set; }
    public string? Content { get; set; }
    public List<string> KeyPoints { get; set; } = [];
    public int SourceCount { get; set; }
    public int SummaryCount { get; set; }
    public int TotalKeyPoints { get; set; }
}

public sealed class ShouldSummarizeResult
{
    public bool ShouldSummarize { get; set; }
    public int CurrentTokenCount { get; set; }
    public int MaxTokens { get; set; }
    public float UsagePercentage { get; set; }
    public int MemoryCount { get; set; }
    public string Recommendation { get; set; } = default!;
}

#endregion
