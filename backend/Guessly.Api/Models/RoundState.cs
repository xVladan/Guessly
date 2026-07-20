namespace Guessly.Api.Models;

/// <summary>
/// State for a single round. The secret word and rank lookup are server-only
/// and must never be serialized into any outbound DTO or SignalR payload.
/// </summary>
public sealed class RoundState
{
    public required int RoundNumber { get; init; }
    public required string SecretWord { get; init; }

    /// <summary>Full word -> rank map for the secret word. NEVER expose this to clients.</summary>
    public required IReadOnlyDictionary<string, int> RankLookup { get; init; }

    public required List<string> TurnOrderPlayerIds { get; init; }
    public int CurrentTurnIndex { get; set; }

    public DateTime RoundDeadlineUtc { get; init; }
    public DateTime TurnDeadlineUtc { get; set; }

    public List<GuessEntry> Guesses { get; } = new();
    public HashSet<string> GuessedWords { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each player's single best (lowest-rank) guess so far this round, for the avatar marker.</summary>
    public Dictionary<string, GuessEntry> PlayerBestGuess { get; } = new();

    /// <summary>Player ids who have already used their one hint this round.</summary>
    public HashSet<string> HintsUsedByPlayerId { get; } = new();

    public bool IsOver { get; set; }
    public string? WinnerPlayerId { get; set; }

    /// <summary>Bumped whenever the round transitions, so stale background timers can detect they're obsolete.</summary>
    public int RoundVersion { get; init; }

    /// <summary>Bumped on every turn change, so a stale turn-timer firing after the turn already advanced is a no-op.</summary>
    public int TurnVersion { get; set; }

    public CancellationTokenSource? TurnTimerCts { get; set; }
    public CancellationTokenSource? RoundTimerCts { get; set; }

    public int TotalAttempts => Guesses.Count;

    public string CurrentTurnPlayerId => TurnOrderPlayerIds[CurrentTurnIndex];
}
