using MemoryIndexer.Configuration;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// <c>AddMemoryIndexer</c> binds the <c>MemoryIndexer</c> section when the container has an <see cref="IConfiguration"/>
/// (a host) and works without one (a console app or tool configuring in code) — it used to throw
/// "No service for type IConfiguration" there on the first options-dependent resolve.
/// </summary>
public class OptionalConfigurationBindingTests
{
    [Fact]
    public void WithoutConfiguration_OptionsResolve_WithCodeSettings()
    {
        var services = new ServiceCollection();
        services.AddMemoryIndexer(o => o.WorkingMemory.Capacity = 5);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value;

        Assert.Equal(5, options.WorkingMemory.Capacity);
    }

    [Fact]
    public void WithConfiguration_SectionBinds_AndCodeSettingsWin()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MemoryIndexer:WorkingMemory:Capacity"] = "7",
            ["MemoryIndexer:Search:DefaultLimit"] = "13",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMemoryIndexer(o => o.WorkingMemory.Capacity = 11);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value;

        Assert.Equal(11, options.WorkingMemory.Capacity);
        Assert.Equal(13, options.Search.DefaultLimit);
    }

    [Fact]
    public void WithConfiguration_OptionsMonitorSeesReload()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MemoryIndexer:WorkingMemory:Capacity"] = "7",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMemoryIndexer();
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<MemoryIndexerOptions>>();
        Assert.Equal(7, monitor.CurrentValue.WorkingMemory.Capacity);

        configuration["MemoryIndexer:WorkingMemory:Capacity"] = "8";
        configuration.Reload();

        Assert.Equal(8, monitor.CurrentValue.WorkingMemory.Capacity);
    }
}
