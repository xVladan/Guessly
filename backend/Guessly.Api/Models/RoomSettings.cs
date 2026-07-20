namespace Guessly.Api.Models;

public sealed class RoomSettings
{
    public int RoundCount { get; set; } = 3;
    public int TurnSeconds { get; set; } = 15;
    public int RoundSeconds { get; set; } = 240;
    public int MinWordLength { get; set; } = 4;
    public int MaxWordLength { get; set; } = 8;

    public const int MinRoundCount = 1;
    public const int MaxRoundCount = 15;
    public const int MinTurnSeconds = 5;
    public const int MaxTurnSeconds = 20;
    public const int MinRoundSeconds = 60;
    public const int MaxRoundSeconds = 600;
    public const int AbsoluteMinWordLength = 3;
    public const int AbsoluteMaxWordLength = 10;

    public string? Validate()
    {
        if (RoundCount < MinRoundCount || RoundCount > MaxRoundCount)
            return $"Round count must be between {MinRoundCount} and {MaxRoundCount}.";
        if (TurnSeconds < MinTurnSeconds || TurnSeconds > MaxTurnSeconds)
            return $"Turn seconds must be between {MinTurnSeconds} and {MaxTurnSeconds}.";
        if (RoundSeconds < MinRoundSeconds || RoundSeconds > MaxRoundSeconds)
            return $"Round seconds must be between {MinRoundSeconds} and {MaxRoundSeconds}.";
        if (MinWordLength < AbsoluteMinWordLength || MaxWordLength > AbsoluteMaxWordLength)
            return $"Word length must be within {AbsoluteMinWordLength}-{AbsoluteMaxWordLength}.";
        if (MinWordLength > MaxWordLength)
            return "Min word length cannot exceed max word length.";
        return null;
    }
}
