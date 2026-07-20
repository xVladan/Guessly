namespace Guessly.Api.Models;

public sealed class Room
{
    public required string Code { get; init; }
    public required string HostPlayerId { get; set; }

    /// <summary>Join order is preserved; disconnected players stay in this list (skipped in rotation) until they leave explicitly.</summary>
    public List<Player> Players { get; } = new();

    public RoomSettings Settings { get; set; } = new();
    public RoomPhase Phase { get; set; } = RoomPhase.Lobby;

    public int CurrentRoundNumber { get; set; }
    public RoundState? CurrentRound { get; set; }
    public HashSet<string> UsedSecretWords { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cumulative score per player id across all rounds this game.</summary>
    public Dictionary<string, int> Scores { get; } = new();

    /// <summary>Guards all mutations of this room's state (players, round, timers).</summary>
    public readonly object Lock = new();

    public List<Player> ConnectedPlayersInJoinOrder =>
        Players.Where(p => p.IsConnected).OrderBy(p => p.JoinOrder).ToList();
}
