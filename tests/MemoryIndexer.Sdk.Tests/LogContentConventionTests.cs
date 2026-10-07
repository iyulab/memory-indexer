using System.Reflection;
using Iyu.Conventions.Testing;
using MemoryIndexer.Sdk.Services;
using MemoryIndexer.Services;
using McpServer.Controllers;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Memory content never reaches a log. A memory store holds users' conversations and the facts drawn from them; logs
/// are shipped elsewhere, kept under a different retention, and outlive a user's deletion request. A log message may
/// carry ids, counts, lengths, types, scores and durations - never a memory's content, a query, a prompt, a model
/// response, an extracted fact or value, a question or answer, a hypothetical document or a summary text.
/// </summary>
/// <remarks>
/// The scan is <c>Iyu.Conventions.Testing</c>'s placeholder-name rule over every <c>[LoggerMessage]</c> template in the
/// shipped assemblies. A count or a length gets a name that says so (<c>{QueryLength}</c>, <c>{FactCount}</c>).
/// </remarks>
public class LogContentConventionTests
{
    private static readonly Assembly[] Libraries =
    [
        typeof(SimpleMemoryService).Assembly,
        typeof(MemoryPromotionBackgroundService).Assembly,
        typeof(MemoryController).Assembly,
    ];

    /// <summary>This domain's names for text, on top of the kit's starting set.</summary>
    private static readonly string[] MemoryContentNames =
    [
        "Hypothetical", "Fact", "Statement", "MemoryContent", "FtsQuery", "ObjectValue", "OldValue", "NewValue",
    ];

    private static readonly Lazy<OperationalLanguageReport> Result = new(() =>
        OperationalLanguage.Scan(Libraries,
            OperationalLanguage.PlaceholderNamed([.. OperationalLanguage.ContentPlaceholderNames, .. MemoryContentNames])));

    [Fact]
    public void LogTemplates_CarryNoMemoryContent() => Result.Value.ShouldBeClean();

    // Positive control: the scan must see the templates it exists to judge.
    [Fact]
    public void Scan_SeesLogTemplates() =>
        Assert.True(Result.Value.LogMessagesRead > 400, $"log templates seen: {Result.Value.LogMessagesRead}");
}
