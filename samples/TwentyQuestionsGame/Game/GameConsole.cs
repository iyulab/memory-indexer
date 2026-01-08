namespace TwentyQuestionsGame.Game;

/// <summary>
/// Console output helper with color support for Alpha/Beta agents.
/// </summary>
public static class GameConsole
{
    private static readonly object _lock = new();

    public static void WriteAlpha(string message, bool newLine = true)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            if (newLine) Console.WriteLine($"[ALPHA] {message}");
            else Console.Write($"[ALPHA] {message}");
            Console.ResetColor();
        }
    }

    public static void WriteBeta(string message, bool newLine = true)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            if (newLine) Console.WriteLine($"[BETA] {message}");
            else Console.Write($"[BETA] {message}");
            Console.ResetColor();
        }
    }

    public static void WriteAlphaMemory(string label, string content)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"       📝 {label}: {content}");
            Console.ResetColor();
        }
    }

    public static void WriteBetaMemory(string label, string content)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"       📝 {label}: {content}");
            Console.ResetColor();
        }
    }

    public static void WriteAlphaRecall(IEnumerable<(float score, string content)> memories, long recallMs = 0)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            var timing = recallMs > 0 ? $" ({recallMs}ms)" : "";
            Console.WriteLine($"       🔍 Recalled memories{timing}:");
            foreach (var (score, content) in memories)
            {
                Console.WriteLine($"          [{score:F2}] {Truncate(content, 80)}");
            }
            Console.ResetColor();
        }
    }

    public static void WriteBetaRecall(IEnumerable<(float score, string content)> memories, long recallMs = 0)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            var timing = recallMs > 0 ? $" ({recallMs}ms)" : "";
            Console.WriteLine($"       🔍 Recalled memories{timing}:");
            foreach (var (score, content) in memories)
            {
                Console.WriteLine($"          [{score:F2}] {Truncate(content, 80)}");
            }
            Console.ResetColor();
        }
    }

    public static void WriteSystem(string message)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static void WriteRoundHeader(int round, int maxRounds)
    {
        lock (_lock)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"{'═'.ToString().PadRight(60, '═')} Round {round}/{maxRounds} {'═'.ToString().PadRight(10, '═')}");
            Console.ResetColor();
        }
    }

    public static void WriteSuccess(string message)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static void WriteWarning(string message)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static void WriteError(string message)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static void WriteStats(string label, string value)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"       {label}: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(value);
            Console.ResetColor();
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }
}
