using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using MemoryIndexer.Sdk.Intelligence.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Extraction;

/// <summary>
/// How the LLM-backed extractors read a model's JSON. A small model wraps the object in a fence or a sentence, leaves a
/// trailing comma or quotes a number: those still read. One that does not read is asked for once more, naming why; when the
/// second answer does not read either, the result says it was a parse error instead of looking like «no facts».
/// </summary>
public sealed class LlmJsonReadingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string OneFact = """
        {
          "contextType": "Direct",
          "overallConfidence": 0.9,
          "reasoning": "first-person statement",
          "facts": [ { "content": "User's name is John", "category": "Identity", "confidence": 0.95, "importance": 0.9, "promotionPath": "FastTrack" } ]
        }
        """;

    private static (LlmFactExtractor Extractor, ITextCompletionService Completion) Extractor(params string[] replies)
    {
        var completion = Substitute.For<ITextCompletionService>();
        completion.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>())
            .Returns(replies[0], replies.Skip(1).ToArray());
        return (new LlmFactExtractor(completion, NullLogger<LlmFactExtractor>.Instance), completion);
    }

    private static FactExtractionContext Turn() => new() { UserId = "u", Content = "My name is John." };

    [Theory]
    [InlineData("```json\n" + OneFact + "\n```")]
    [InlineData("Here is the analysis:\n" + OneFact + "\nLet me know if you need more.")]
    public async Task AnObjectInAFenceOrASentence_Reads_WithOneCall(string reply)
    {
        var (extractor, completion) = Extractor(reply);

        var result = await extractor.ExtractAsync(Turn(), Ct);

        Assert.Equal("User's name is John", Assert.Single(result.Facts).Content);
        await completion.Received(1).CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATrailingCommaAndAQuotedNumber_Read()
    {
        var (extractor, _) = Extractor("""
            { "contextType": "Direct", "overallConfidence": "0.9", "facts": [ { "content": "User likes tea", "confidence": "0.85", }, ], }
            """);

        var result = await extractor.ExtractAsync(Turn(), Ct);

        Assert.Equal(0.85f, Assert.Single(result.Facts).Confidence);
        Assert.Equal(0.9f, result.OverallConfidence);
    }

    [Fact]
    public async Task AnAnswerThatDoesNotRead_IsAskedForOnceMore_WithTheReason()
    {
        // The template's old form: a range where a number belongs
        var (extractor, completion) = Extractor("""{ "contextType": "Direct", "overallConfidence": 0.0-1.0, "facts": [] }""", OneFact);

        var result = await extractor.ExtractAsync(Turn(), Ct);

        Assert.Single(result.Facts);
        var prompts = completion.ReceivedCalls().Select(c => (string)c.GetArguments()[0]!).ToList();
        Assert.Equal(2, prompts.Count);
        Assert.Contains("could not be read as one JSON object", prompts[1], StringComparison.Ordinal);
        Assert.StartsWith(prompts[0], prompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReplyCutOffBeforeTheObjectCloses_IsAskedForAgain_AndReportedAsAParseError()
    {
        var (extractor, completion) = Extractor("""{ "contextType": "Direct", "facts": [ { "content": "User's na""", """{ "facts": [""");

        var result = await extractor.ExtractAsync(Turn(), Ct);

        Assert.Empty(result.Facts);
        Assert.Contains("Parse error", result.Reasoning, StringComparison.Ordinal);
        Assert.Contains("not closed", result.Reasoning, StringComparison.Ordinal);
        await completion.Received(2).CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANestedObjectIsNotReadAsTheWhole()
    {
        // The outer object does not read (a range for a number); its fact must not pass as an empty result on its own
        var (extractor, completion) = Extractor(
            """{ "overallConfidence": 0.0-1.0, "facts": [ { "content": "User likes tea", "confidence": 0.8 } ] }""",
            OneFact);

        var result = await extractor.ExtractAsync(Turn(), Ct);

        Assert.Equal("User's name is John", Assert.Single(result.Facts).Content);
        await completion.Received(2).CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryJsonRequest_AsksForAJsonAnswer()
    {
        var completion = Substitute.For<ITextCompletionService>();
        completion.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>()).Returns("{}");

        await new LlmFactExtractor(completion, NullLogger<LlmFactExtractor>.Instance).ExtractAsync(Turn(), Ct);
        await new LlmFactExtractor(completion, NullLogger<LlmFactExtractor>.Instance).ValidateAsync(
            new UserFact { Content = "User lives in Seoul" }, [new MemoryUnit { UserId = "u", Content = "User lives in Busan" }], Ct);
        await new LlmKnowledgeExtractor(completion, NullLogger<LlmKnowledgeExtractor>.Instance).ExtractAsync(
            new KnowledgeExtractionContext { UserId = "u", Question = "Is the sky blue?", Answer = "yes" }, Ct);
        await new LlmConflictDetector(completion, NullLogger<LlmConflictDetector>.Instance).AnalyzeAsync(
            new MemoryUnit { UserId = "u", Content = "likes tea" }, new MemoryUnit { UserId = "u", Content = "dislikes tea" }, Ct);

        var formats = completion.ReceivedCalls().Select(c => ((TextCompletionOptions)c.GetArguments()[1]!).ResponseFormat).ToList();
        Assert.NotEmpty(formats);
        Assert.All(formats, f => Assert.Equal(CompletionResponseFormat.Json, f));
    }
}
