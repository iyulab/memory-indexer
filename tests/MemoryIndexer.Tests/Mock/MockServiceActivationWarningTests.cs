using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Mock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Tests.Mock;

/// <summary>
/// Pilot regression test for the "silent failure" convention (docs/CONVENTIONS.md §2,
/// ironhive-umbrella BD-20260905-03): a Mock service silently active in production looks like a
/// working embedding/completion pipeline while actually returning non-semantic placeholders. Both
/// Mock services log a Warning on construction specifically to prevent that — this test pins that
/// behavior so a future refactor cannot drop it unnoticed.
/// </summary>
public class MockServiceActivationWarningTests
{
    [Fact]
    public void MockEmbeddingService_Construction_LogsActivationWarning()
    {
        var logger = new RecordingLogger<MockEmbeddingService>();
        var options = Options.Create(new MemoryIndexerOptions());

        _ = new MockEmbeddingService(options, logger);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("MockEmbeddingService is active"));
    }

    [Fact]
    public void MockTextCompletionService_Construction_LogsActivationWarning()
    {
        var logger = new RecordingLogger<MockTextCompletionService>();

        _ = new MockTextCompletionService(logger);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("MockTextCompletionService is active"));
    }

    /// <summary>발화한 로그를 그대로 담아 두는 최소 로거 — 생성된 메시지 본문까지 봐야 하므로 substitute 대신 쓴다.</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
