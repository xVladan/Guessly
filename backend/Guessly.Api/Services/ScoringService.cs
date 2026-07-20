namespace Guessly.Api.Services;

public static class ScoringService
{
    public const int WinnerMinScore = 20;
    public const int WinnerMaxScore = 100;
    public const int TimeoutMaxScore = 15;

    /// <summary>winnerScore = clamp(120 - totalAttempts * 3, 20, 100)</summary>
    public static int ComputeWinnerScore(int totalAttemptsThisRound)
    {
        var raw = 120 - totalAttemptsThisRound * 3;
        return Math.Clamp(raw, WinnerMinScore, WinnerMaxScore);
    }

    /// <summary>timeoutScore = round(15 * max(0, 1 - rank/1000)); 0 if player never guessed.</summary>
    public static int ComputeTimeoutScore(int? bestRank)
    {
        if (bestRank is null)
            return 0;

        var raw = TimeoutMaxScore * Math.Max(0.0, 1.0 - bestRank.Value / 1000.0);
        return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
    }
}
