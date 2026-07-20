using Guessly.Api.Models;

namespace Guessly.Api.Dtos;

public sealed record PlayerDto(string Id, string Name, string Avatar, bool IsConnected, int TotalScore, int JoinOrder)
{
    public static PlayerDto From(Player p) => new(p.Id, p.Name, p.Avatar, p.IsConnected, p.TotalScore, p.JoinOrder);
}

public sealed record RoomStateDto(
    string Code,
    string HostPlayerId,
    string Phase,
    RoomSettings Settings,
    List<PlayerDto> Players,
    int CurrentRoundNumber);

public sealed record JoinResultDto(RoomStateDto Room, string YourPlayerId);

public sealed record RoundStartedDto(
    int RoundNumber,
    int TotalRounds,
    int SecretWordLength,
    List<string> TurnOrderPlayerIds,
    DateTime RoundDeadlineUtc,
    string ActivePlayerId,
    DateTime TurnDeadlineUtc);

public sealed record TurnChangedDto(string ActivePlayerId, DateTime TurnDeadlineUtc, string? LastPassedPlayerId);

public sealed record GuessResultDto(
    string Word,
    string? PlayerId,
    int Rank,
    bool IsCorrect,
    int AttemptNumber,
    bool IsHint);

public sealed record GuessRejectedDto(string Reason, bool TurnEnded);

public sealed record RoundEndedDto(
    int RoundNumber,
    string? WinnerPlayerId,
    string SecretWord,
    int TotalAttempts,
    Dictionary<string, int> RoundScores,
    Dictionary<string, int> CumulativeScores);

public sealed record GameEndedDto(Dictionary<string, int> FinalScores, string? WinnerPlayerId);
