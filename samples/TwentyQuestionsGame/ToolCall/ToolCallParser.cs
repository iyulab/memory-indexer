using System.Text.RegularExpressions;

namespace TwentyQuestionsGame.ToolCall;

/// <summary>
/// Parses <![CDATA[<tool_call>]]> blocks from LLM responses.
/// </summary>
public sealed partial class ToolCallParser
{
    [GeneratedRegex(@"<tool_call>\s*(\w+)\((.*?)\)\s*</tool_call>", RegexOptions.Singleline)]
    private static partial Regex ToolCallRegex();

    [GeneratedRegex(@"(\w+)\s*=\s*(?:""([^""]*)""|(\d+\.?\d*))", RegexOptions.None)]
    private static partial Regex ArgumentRegex();

    public static IReadOnlyList<ParsedToolCall> Parse(string llmResponse)
    {
        var results = new List<ParsedToolCall>();

        foreach (Match match in ToolCallRegex().Matches(llmResponse))
        {
            var toolName = match.Groups[1].Value;
            var argsString = match.Groups[2].Value;
            var arguments = ParseArguments(argsString);

            results.Add(new ParsedToolCall(toolName, arguments, match.Value));
        }

        return results;
    }

    public static bool HasToolCalls(string llmResponse)
    {
        return ToolCallRegex().IsMatch(llmResponse);
    }

    public static string RemoveToolCalls(string llmResponse)
    {
        return ToolCallRegex().Replace(llmResponse, "").Trim();
    }

    private static Dictionary<string, string> ParseArguments(string argsString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ArgumentRegex().Matches(argsString))
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            result[key] = value;
        }

        return result;
    }
}

/// <summary>
/// Represents a parsed tool call from LLM output.
/// </summary>
public sealed record ParsedToolCall(
    string ToolName,
    Dictionary<string, string> Arguments,
    string RawText)
{
    public string GetArgument(string name, string defaultValue = "")
        => Arguments.TryGetValue(name, out var value) ? value : defaultValue;

    public float GetFloatArgument(string name, float defaultValue = 0.5f)
        => float.TryParse(GetArgument(name), out var value) ? value : defaultValue;

    public int GetIntArgument(string name, int defaultValue = 10)
        => int.TryParse(GetArgument(name), out var value) ? value : defaultValue;
}
