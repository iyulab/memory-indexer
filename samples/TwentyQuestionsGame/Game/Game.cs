namespace TwentyQuestionsGame.Game;

/// <summary>
/// Game configuration and constants.
/// </summary>
public static class GameConfiguration
{
    public const int MaxRounds = 20;
    public const string AlphaUserId = "alpha";
    public const string BetaUserId = "beta";
    public const string AlphaSessionId = "alpha-session";
    public const string BetaSessionId = "beta-session";
}

/// <summary>
/// Tracks game state: round, messages, and outcome.
/// </summary>
public sealed class GameState
{
    private readonly List<(string Question, string Answer)> _questionHistory = [];

    public int CurrentRound { get; private set; } = 1;
    public bool IsGameOver { get; private set; }
    public bool BetaWon { get; private set; }
    public string LastAlphaResponse { get; private set; } = "The game has started. Ask your first question!";
    public string? LastBetaQuestion { get; private set; }

    /// <summary>
    /// All previous Q&A pairs for duplicate detection.
    /// </summary>
    public IReadOnlyList<(string Question, string Answer)> QuestionHistory => _questionHistory;

    public void RecordBetaQuestion(string question) => LastBetaQuestion = question;

    public void RecordAlphaResponse(string response)
    {
        LastAlphaResponse = response;

        // Record Q&A pair when we have both
        if (LastBetaQuestion != null)
        {
            _questionHistory.Add((LastBetaQuestion, response));
        }
    }

    public void EndGame(bool betaWins)
    {
        IsGameOver = true;
        BetaWon = betaWins;
    }

    public void NextRound()
    {
        if (!IsGameOver)
        {
            CurrentRound++;
            if (CurrentRound > GameConfiguration.MaxRounds)
                EndGame(betaWins: false);
        }
    }
}
