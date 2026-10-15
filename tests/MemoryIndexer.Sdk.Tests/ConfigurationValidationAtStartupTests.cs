using System.Diagnostics.Metrics;
using System.Reflection;
using MemoryIndexer.Configuration;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Sdk.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// The configuration rules <c>AddMemoryIndexer()</c> registers run when the host starts: a value out of range stops the host
/// with the setting named, and the defaults start.
/// </summary>
public class ConfigurationValidationAtStartupTests
{
    [Fact]
    public void TheDefaults_PassTheRules()
    {
        var result = new ConfigurationValidator().Validate(new MemoryIndexerOptions());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.PropertyPath}: {e.Message}")));
    }

    /// <summary>The sample configuration files a reader copies pass the rules the host now runs at startup.</summary>
    [Theory]
    [InlineData("appsettings.mcp-server.json")]
    [InlineData("appsettings.sdk-embedded.json")]
    public void TheSampleConfigurations_PassTheRules(string file)
    {
        var path = Path.Combine(RepositoryRoot(), "samples", file);
        var options = new MemoryIndexerOptions();
        new ConfigurationBuilder().AddJsonFile(path).Build().GetSection(MemoryIndexerOptions.SectionName).Bind(options);

        var result = new ConfigurationValidator().Validate(options);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.PropertyPath}: {e.Message}")));
    }

    [Fact]
    public async Task AValueOutOfRange_FailsTheHostAtStartup_NamingTheSetting()
    {
        using var host = HostWith(new Dictionary<string, string?> { ["MemoryIndexer:Embedding:Dimensions"] = "0" });

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("MemoryIndexer:Embedding:Dimensions", ex.Message, StringComparison.Ordinal);
        Assert.Contains("must be positive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodeConfiguredValueOutOfRange_FailsTheHostAtStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMemoryIndexer(o => o.Search.DefaultLimit = 0);
        using var host = builder.Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("MemoryIndexer:Search:DefaultLimit", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDefaults_StartTheHost()
    {
        using var host = HostWith(new Dictionary<string, string?> { ["MemoryIndexer:Embedding:Provider"] = "Mock" });

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void OneCapacitySetting_SizesTheShortTierCache()
    {
        using var host = HostWith(new Dictionary<string, string?> { ["MemoryIndexer:WorkingMemory:Capacity"] = "5" });

        Assert.Equal(5, host.Services.GetRequiredService<MemoryIndexer.Interfaces.IShortTermMemory>().Capacity);
    }

    [Theory]
    [InlineData("MemoryIndexer:VCM:WorkingMemory:Capacity", "MemoryIndexer:WorkingMemory:Capacity")]
    [InlineData("MemoryIndexer:VCM:SemanticStore:MinConfirmationCount", "MemoryIndexer:ArchivePromotion:MinConfirmationCount")]
    [InlineData("MemoryIndexer:VCM:SemanticStore:MinConfidenceThreshold", "MemoryIndexer:ArchivePromotion:MinConfidenceThreshold")]
    public async Task ASettingThatMoved_FailsTheHostAtStartup_NamingWhereItLivesNow(string old, string now)
    {
        using var host = HostWith(new Dictionary<string, string?> { [old] = "3" });

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains($"{old} is no longer read: set {now} instead", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRetiredStoreDimension_FailsTheHostAtStartup_NamingWhereTheSizeComesFrom()
    {
        using var host = HostWith(new Dictionary<string, string?> { ["MemoryIndexer:Storage:VectorDimensions"] = "1024" });

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("MemoryIndexer:Storage:VectorDimensions is no longer read", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IEmbeddingService.Dimensions", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every meter the packages record on is in <see cref="MemoryIndexerTelemetry.MeterNames"/>, which
    /// <c>AddMemoryIndexerObservability</c> subscribes — the evaluation meter was recorded on and never exported.
    /// </summary>
    [Fact]
    public void EveryMeterThePackagesDeclare_IsSubscribedByTheObservabilityExtension()
    {
        var meters = new[] { typeof(MemoryIndexerOptions).Assembly, typeof(MemoryIndexerTelemetry).Assembly }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => f.FieldType == typeof(Meter))
            .Select(f => ((Meter)f.GetValue(null)!).Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains(MemoryIndexerTelemetry.EvaluationMeterName, meters);
        Assert.All(meters, name => Assert.Contains(name, MemoryIndexerTelemetry.MeterNames));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MemoryIndexer.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MemoryIndexer.slnx not found above the test output directory");
    }

    private static IHost HostWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddMemoryIndexer();
        return builder.Build();
    }
}
