namespace Guessly.Api.Models;

public sealed class GuessEntry
{
    public required string Word { get; init; }
    public required string PlayerId { get; init; }
    public required int Rank { get; init; }
    public required int AttemptNumber { get; init; }
    public bool IsHint { get; init; }
    public DateTime SubmittedAtUtc { get; init; } = DateTime.UtcNow;
}
