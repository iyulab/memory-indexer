using Iyu.Conventions.Testing;
using MemoryIndexer.Sdk.Services;
using MemoryIndexer.Services;
using McpServer.Controllers;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Operational text — every <c>[LoggerMessage]</c> template and every exception message — is ASCII. Operators grep it,
/// paste it into issues and search it in log pipelines whose tokenizers split on Latin word boundaries; a dash or an
/// arrow outside ASCII is as opaque there as a Korean word. The rule is the umbrella's logging convention; the scan is
/// <c>Iyu.Conventions.Testing</c>'s, shared with the other repositories. It replaces the per-type Hangul-only tests.
/// </summary>
public class OperationalLanguageConventionTests
{
    private static readonly Lazy<OperationalLanguageReport> Result = new(() => OperationalLanguage.Scan(
        [typeof(SimpleMemoryService).Assembly, typeof(MemoryPromotionBackgroundService).Assembly, typeof(MemoryController).Assembly],
        OperationalLanguage.NonAscii));

    [Fact]
    public void LogTemplatesAndExceptionMessages_AreAscii()
    {
        var findings = Result.Value.Findings;
        Assert.True(findings.Count == 0,
            "Non-ASCII operational text:\n" + string.Join("\n", findings.Select(f => $"  [{f.Kind}] {f.Location}: {f.Text}")));
    }

    // Positive control: the scan must see the operational text it exists to judge.
    [Fact]
    public void Scan_SeesLogTemplatesAndExceptionMessages()
    {
        Assert.True(Result.Value.LogMessagesRead > 20, $"log templates seen: {Result.Value.LogMessagesRead}");
        Assert.True(Result.Value.ExceptionLiteralsRead > 10, $"exception messages seen: {Result.Value.ExceptionLiteralsRead}");
    }
}
