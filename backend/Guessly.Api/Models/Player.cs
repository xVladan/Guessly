namespace Guessly.Api.Models;

public sealed class Player
{
    public required string Id { get; init; }
    public required string ConnectionId { get; set; }
    public required string Name { get; set; }
    public required string Avatar { get; set; }
    public bool IsConnected { get; set; } = true;
    public int JoinOrder { get; init; }
    public int TotalScore { get; set; }
}
