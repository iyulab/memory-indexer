using System.Reflection;
using System.Text.RegularExpressions;
using MemoryIndexer.Sdk.Services;
using MemoryIndexer.Services;
using McpServer.Controllers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Memory content never reaches a log. A memory store holds users' conversations and the facts drawn from them; logs
/// are shipped elsewhere, kept under a different retention, and outlive a user's deletion request. A log message may
/// carry ids, counts, lengths, types, scores and durations - never a memory's content, a query, a prompt, a model
/// response, an extracted fact or value, a question or answer, a hypothetical document or a summary text.
/// </summary>
/// <remarks>
/// The scan reads every <c>[LoggerMessage]</c> template in the shipped assemblies and rejects placeholders whose name
/// says they carry such text. A count or a length gets a name that says so (<c>{QueryLength}</c>, <c>{FactCount}</c>).
/// </remarks>
public partial class LogContentConventionTests
{
    private static readonly Assembly[] Libraries =
    [
        typeof(SimpleMemoryService).Assembly,
        typeof(MemoryPromotionBackgroundService).Assembly,
        typeof(MemoryController).Assembly,
    ];

    /// <summary>Placeholder names that carry user or model text. Compared case-insensitively, whole name only.</summary>
    private static readonly HashSet<string> ContentBearingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content", "Query", "Response", "Question", "Answer", "Hypothetical", "Fact", "Text", "Preview", "Summary",
        "Statement", "Prompt", "Input", "Snippet", "MemoryContent", "FtsQuery",
        "ObjectValue", "OldValue", "NewValue",
    };

    private static readonly Lazy<List<(string Location, string Template)>> Templates = new(ReadTemplates);

    [Fact]
    public void LogTemplates_CarryNoMemoryContent()
    {
        var findings = Templates.Value
            .SelectMany(t => Placeholders(t.Template)
                .Where(ContentBearingNames.Contains)
                .Select(name => $"  {t.Location}: {{{name}}} in \"{t.Template}\""))
            .ToList();

        Assert.True(findings.Count == 0,
            "Log templates that carry memory content (log a length, a count or an id instead):\n" + string.Join("\n", findings));
    }

    // Positive control: the scan must see the templates it exists to judge.
    [Fact]
    public void Scan_SeesLogTemplates()
    {
        Assert.True(Templates.Value.Count > 400, $"log templates seen: {Templates.Value.Count}");
        Assert.Contains(Templates.Value, t => Placeholders(t.Template).Any());
    }

    private static List<(string Location, string Template)> ReadTemplates()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var templates = new List<(string, string)>();
        foreach (var assembly in Libraries)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(all))
                {
                    var attribute = method.GetCustomAttribute<LoggerMessageAttribute>();
                    if (attribute?.Message is { Length: > 0 } message)
                    {
                        templates.Add(($"{type.FullName}.{method.Name}", message));
                    }
                }
            }
        }

        return templates;
    }

    private static IEnumerable<string> Placeholders(string template) =>
        PlaceholderPattern().Matches(template.Replace("{{", "", StringComparison.Ordinal))
            .Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\{([A-Za-z_]\w*)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
