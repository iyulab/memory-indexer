using MemoryIndexer.Mcp.Tools;
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
}
