using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Retrieval;

/// <summary>
/// Assembles context from retrieval results with adaptive fidelity.
/// </summary>
/// <remarks>
/// Based on AFM (Adaptive Focus Memory) research:
/// - Full: Complete content for highest-relevance items
/// - Compressed: Summarized content for secondary items
/// - Placeholder: Minimal reference for low-priority items
/// </remarks>
public sealed class AdaptiveContextAssembler : IAdaptiveContextAssembler
{
    private readonly ILogger<AdaptiveContextAssembler> _logger;

    // Average characters per token (approximation)
    private const int CharsPerToken = 4;

    // Compression targets by fidelity
    private static readonly Dictionary<ContextFidelity, float> CompressionTargets = new()
    {
        [ContextFidelity.Full] = 1.0f,        // No compression
        [ContextFidelity.Compressed] = 0.3f, // 30% of original
        [ContextFidelity.Placeholder] = 0.1f  // 10% of original
    };

    public AdaptiveContextAssembler(ILogger<AdaptiveContextAssembler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AssembledContext> AssembleAsync(
        TieredRetrievalResult retrievalResult,
        ContextAssemblyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ContextAssemblyOptions();
        var stopwatch = Stopwatch.StartNew();
        var compressionStopwatch = new Stopwatch();

        var sb = new StringBuilder();
        var tokenCount = 0;
        var memoryCount = 0;
        var excludedCount = 0;
        var compressionCount = 0;
        var tokensSaved = 0;

        var fidelityBreakdown = new Dictionary<ContextFidelity, int>
        {
            [ContextFidelity.Full] = 0,
            [ContextFidelity.Compressed] = 0,
            [ContextFidelity.Placeholder] = 0
        };
        var tierBreakdown = new Dictionary<MemoryTier, int>();

        // Add custom header if provided
        if (!string.IsNullOrEmpty(options.CustomHeader))
        {
            sb.AppendLine(options.CustomHeader);
            sb.AppendLine();
        }

        // Calculate budget allocations
        var fullBudget = (int)(options.MaxTokens * options.FullFidelityRatio);
        var compressedBudget = (int)(options.MaxTokens * options.CompressedRatio);
        var placeholderBudget = options.MaxTokens - fullBudget - compressedBudget;

        // Process memories by fidelity level
        foreach (var memory in retrievalResult.MergedResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tier = memory.SourceTier;
            if (!tierBreakdown.ContainsKey(tier))
                tierBreakdown[tier] = 0;

            // Check budget for this fidelity level
            var currentBudget = memory.Fidelity switch
            {
                ContextFidelity.Full => fullBudget,
                ContextFidelity.Compressed => compressedBudget,
                _ => placeholderBudget
            };

            var currentUsed = fidelityBreakdown[memory.Fidelity];
            if (currentUsed >= currentBudget)
            {
                excludedCount++;
                continue;
            }

            // Compress content based on fidelity
            compressionStopwatch.Start();
            var content = await CompressAsync(memory.Memory, memory.Fidelity, cancellationToken);
            compressionStopwatch.Stop();

            var contentTokens = EstimateTokens(content);

            // Check if adding this exceeds budget
            if (currentUsed + contentTokens > currentBudget)
            {
                // Try to fit partial
                var remainingTokens = currentBudget - currentUsed;
                if (remainingTokens > 50) // Minimum useful content
                {
                    content = TruncateToTokens(content, remainingTokens);
                    contentTokens = remainingTokens;
                }
                else
                {
                    excludedCount++;
                    continue;
                }
            }

            // Add to output
            AppendMemory(sb, memory, content, options);

            // Update counters
            tokenCount += contentTokens;
            memoryCount++;
            fidelityBreakdown[memory.Fidelity] += contentTokens;
            tierBreakdown[tier] += contentTokens;

            if (memory.Fidelity != ContextFidelity.Full)
            {
                compressionCount++;
                tokensSaved += memory.EstimatedTokens - contentTokens;
            }
        }

        // Add graph context if present and requested
        if (options.IncludeGraphContext && retrievalResult.GraphContext != null)
        {
            var graphTokens = EstimateTokens(retrievalResult.GraphContext.FormattedContext);
            if (tokenCount + graphTokens <= options.MaxTokens)
            {
                sb.AppendLine();
                sb.AppendLine(retrievalResult.GraphContext.FormattedContext);
                tokenCount += graphTokens;
            }
        }

        // Add custom footer if provided
        if (!string.IsNullOrEmpty(options.CustomFooter))
        {
            sb.AppendLine();
            sb.AppendLine(options.CustomFooter);
        }

        stopwatch.Stop();

        var avgCompression = compressionCount > 0
            ? 1.0f - ((float)tokensSaved / (compressionCount * 100)) // Approximate
            : 1.0f;

        _logger.LogDebug(
            "Context assembled: {MemoryCount} memories, {TokenCount} tokens, {CompressionCount} compressed in {Duration}ms",
            memoryCount, tokenCount, compressionCount, stopwatch.ElapsedMilliseconds);

        return new AssembledContext
        {
            Content = sb.ToString(),
            TokenCount = tokenCount,
            FidelityBreakdown = fidelityBreakdown,
            TierBreakdown = tierBreakdown,
            MemoryCount = memoryCount,
            ExcludedCount = excludedCount,
            WasTruncated = excludedCount > 0,
            Statistics = new ContextAssemblyStatistics
            {
                Duration = stopwatch.Elapsed,
                CompressionDuration = compressionStopwatch.Elapsed,
                CompressionCount = compressionCount,
                TokensSaved = tokensSaved,
                AverageCompressionRatio = avgCompression
            }
        };
    }

    /// <inheritdoc />
    public Task<string> CompressAsync(
        MemoryUnit memory,
        ContextFidelity fidelity,
        CancellationToken cancellationToken = default)
    {
        var content = memory.Content;

        var result = fidelity switch
        {
            ContextFidelity.Full => content,
            ContextFidelity.Compressed => CompressContent(content),
            ContextFidelity.Placeholder => CreatePlaceholder(memory),
            _ => content
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public int EstimateTokens(string content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        return (content.Length + CharsPerToken - 1) / CharsPerToken;
    }

    private static string CompressContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Simple compression: extract first sentence and key phrases
        var sentences = content.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);

        if (sentences.Length == 0)
            return content;

        if (sentences.Length == 1)
            return TruncateWithEllipsis(sentences[0].Trim(), 100);

        // Take first sentence + summary indicator
        var firstSentence = sentences[0].Trim();
        var remainingCount = sentences.Length - 1;

        if (firstSentence.Length > 150)
            firstSentence = TruncateWithEllipsis(firstSentence, 150);

        return remainingCount > 0
            ? $"{firstSentence}. [{remainingCount} more sentences...]"
            : firstSentence + ".";
    }

    private static string CreatePlaceholder(MemoryUnit memory)
    {
        var type = memory.Type.ToString().ToLowerInvariant();
        var age = DateTime.UtcNow - memory.CreatedAt;
        var ageStr = age.TotalDays switch
        {
            < 1 => "today",
            < 7 => $"{(int)age.TotalDays}d ago",
            < 30 => $"{(int)(age.TotalDays / 7)}w ago",
            _ => $"{(int)(age.TotalDays / 30)}mo ago"
        };

        // Extract first few words as hint
        var words = memory.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hint = string.Join(" ", words.Take(5));
        if (words.Length > 5)
            hint += "...";

        return $"[{type}: \"{hint}\" - {ageStr}]";
    }

    private static string TruncateWithEllipsis(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }

    private static string TruncateToTokens(string content, int maxTokens)
    {
        var maxChars = maxTokens * CharsPerToken;
        if (content.Length <= maxChars)
            return content;

        // Try to break at word boundary
        var truncated = content[..maxChars];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxChars * 0.8) // If reasonable break point
            truncated = truncated[..lastSpace];

        return truncated + "...";
    }

    private void AppendMemory(
        StringBuilder sb,
        ScoredMemory memory,
        string content,
        ContextAssemblyOptions options)
    {
        switch (options.Format)
        {
            case ContextFormat.Markdown:
                AppendMarkdown(sb, memory, content, options);
                break;
            case ContextFormat.PlainText:
                AppendPlainText(sb, memory, content, options);
                break;
            case ContextFormat.Xml:
                AppendXml(sb, memory, content, options);
                break;
            case ContextFormat.Json:
                AppendJson(sb, memory, content, options);
                break;
        }
    }

    private static void AppendMarkdown(
        StringBuilder sb,
        ScoredMemory memory,
        string content,
        ContextAssemblyOptions options)
    {
        if (options.IncludeTierHeaders)
        {
            var tierEmoji = memory.SourceTier switch
            {
                MemoryTier.Working => "💭",
                MemoryTier.Session => "📝",
                MemoryTier.User => "👤",
                _ => "📌"
            };
            sb.AppendLine($"**{tierEmoji} {memory.SourceTier}**");
        }

        if (options.IncludeMetadata)
        {
            sb.AppendLine($"*Type: {memory.Memory.Type}, Created: {memory.Memory.CreatedAt:g}*");
        }

        sb.AppendLine(content);
        sb.AppendLine();
    }

    private static void AppendPlainText(
        StringBuilder sb,
        ScoredMemory memory,
        string content,
        ContextAssemblyOptions options)
    {
        if (options.IncludeTierHeaders)
            sb.AppendLine($"[{memory.SourceTier}]");

        sb.AppendLine(content);
        sb.AppendLine();
    }

    private static void AppendXml(
        StringBuilder sb,
        ScoredMemory memory,
        string content,
        ContextAssemblyOptions options)
    {
        sb.AppendLine($"<memory tier=\"{memory.SourceTier}\" type=\"{memory.Memory.Type}\">");
        sb.AppendLine($"  <content>{System.Security.SecurityElement.Escape(content)}</content>");
        if (options.IncludeMetadata)
        {
            sb.AppendLine($"  <created>{memory.Memory.CreatedAt:O}</created>");
            sb.AppendLine($"  <relevance>{memory.RelevanceScore:F2}</relevance>");
        }
        sb.AppendLine("</memory>");
    }

    private static void AppendJson(
        StringBuilder sb,
        ScoredMemory memory,
        string content,
        ContextAssemblyOptions options)
    {
        var obj = new Dictionary<string, object>
        {
            ["tier"] = memory.SourceTier.ToString(),
            ["content"] = content
        };

        if (options.IncludeMetadata)
        {
            obj["type"] = memory.Memory.Type.ToString();
            obj["created"] = memory.Memory.CreatedAt;
            obj["relevance"] = memory.RelevanceScore;
        }

        sb.AppendLine(JsonSerializer.Serialize(obj));
    }
}
