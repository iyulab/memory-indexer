using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// Extension methods for configuring MCP server with Memory Indexer.
/// </summary>
public static class McpServerExtensions
{
    /// <summary>
    /// Adds Memory Indexer MCP tools to the server builder.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithMemoryTools(this IMcpServerBuilder builder)
    {
        return builder.WithTools<MemoryTools>();
    }

    /// <summary>
    /// Adds Context Budget API MCP tools to the server builder.
    /// Phase v0.9.0: Token-aware context building tools.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithContextTools(this IMcpServerBuilder builder)
    {
        return builder.WithTools<ContextTools>();
    }

    /// <summary>
    /// Adds Fact Extraction MCP tools to the server builder.
    /// Phase v0.9.1: AI-based fact extraction and user profile management.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithFactExtractionTools(this IMcpServerBuilder builder)
    {
        return builder.WithTools<FactExtractionTools>();
    }

    /// <summary>
    /// Adds all Memory Indexer MCP tools to the server builder.
    /// Includes: MemoryTools, ContextTools, and all advanced tools.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithAllMemoryIndexerTools(this IMcpServerBuilder builder)
    {
        return builder
            .WithTools<MemoryTools>()
            .WithTools<ContextTools>()
            .WithTools<FactExtractionTools>()
            .WithTools<AdvancedMemoryTools>()
            .WithTools<AutonomousMemoryTools>()
            .WithTools<BackupRestoreTools>()
            .WithTools<ConflictResolutionTools>()
            .WithTools<AdaptiveRetrievalTools>()
            .WithTools<GraphTraversalTools>()
            .WithTools<KnowledgeGraphTools>()
            .WithTools<ResourceManagementTools>()
            .WithTools<SecurityTools>()
            .WithTools<SelfEditingMemoryTools>();
    }
}
