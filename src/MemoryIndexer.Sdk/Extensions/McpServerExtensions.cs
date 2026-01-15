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
    /// Adds Fact Conflict Resolution MCP tools to the server builder.
    /// Phase v0.9.2: Category-specific conflict detection and resolution.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithFactConflictTools(this IMcpServerBuilder builder)
    {
        return builder.WithTools<FactConflictTools>();
    }

    /// <summary>
    /// Adds Profile Evolution MCP tools to the server builder.
    /// Phase v0.10.0: User profile evolution, inference, snapshots, and GDPR export.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithProfileEvolutionTools(this IMcpServerBuilder builder)
    {
        return builder.WithTools<ProfileEvolutionTools>();
    }

    /// <summary>
    /// Adds Retention Policy MCP tools to the server builder.
    /// Phase v0.11.0: Retention policy management and cleanup operations.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithRetentionPolicyTools(this IMcpServerBuilder builder)
    {
        return builder.WithTools<RetentionPolicyTools>();
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
            .WithTools<FactConflictTools>()
            .WithTools<ProfileEvolutionTools>()
            .WithTools<RetentionPolicyTools>()
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
