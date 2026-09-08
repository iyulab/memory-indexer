namespace McpServer;

/// <summary>
/// Resolves the transport the server runs from its command line. The HTTP mode serves the
/// Streamable HTTP transport (MCP 2026-07-28); <c>--sse</c> is accepted as a deprecated alias so
/// existing launch configurations keep working, with a warning naming the replacement.
/// </summary>
public static class McpServerTransport
{
    /// <summary>Transport mode value for stdio (the default).</summary>
    public const string Stdio = "stdio";

    /// <summary>Transport mode value for Streamable HTTP.</summary>
    public const string Http = "http";

    /// <summary>The transport name reported by the info endpoint.</summary>
    public const string StreamableHttpLabel = "streamable-http";

    /// <summary>The deprecated alias of <c>--http</c>.</summary>
    public const string DeprecatedSseFlag = "--sse";

    /// <summary>Result of <see cref="Parse"/>.</summary>
    /// <param name="Mode"><see cref="Stdio"/> or <see cref="Http"/>.</param>
    /// <param name="DeprecationWarning">A warning to print when a deprecated flag selected the mode; otherwise <c>null</c>.</param>
    public sealed record Resolution(string Mode, string? DeprecationWarning);

    /// <summary>Resolves the transport mode from the command line.</summary>
    public static Resolution Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var http = args.Contains("--http", StringComparer.Ordinal);
        var sse = args.Contains(DeprecatedSseFlag, StringComparer.Ordinal);

        if (!http && !sse)
            return new Resolution(Stdio, null);

        var warning = sse
            ? $"WARNING: {DeprecatedSseFlag} is a deprecated alias for --http and will be removed in a future release. " +
              "The server speaks the Streamable HTTP transport (MCP 2026-07-28); it has never served the deprecated HTTP+SSE transport."
            : null;

        return new Resolution(Http, warning);
    }
}
