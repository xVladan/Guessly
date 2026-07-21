using System.Collections.Concurrent;
using Guessly.Api.Dtos;
using Guessly.Api.Hubs;
using Guessly.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace Guessly.Api.Services;

public sealed class GameEngineException(string message) : Exception(message);

/// <summary>
/// Central owner of all room state and game logic. The SignalR hub is a thin
/// adapter that delegates into this class; this class also drives
/// server-authoritative timers and broadcasts directly via IHubContext, so
/// the same code path handles both client-triggered and timer-triggered
/// state transitions.
/// </summary>
public sealed class GameEngine(
    IHubContext<GameHub> hubContext,
    EmbeddingService embeddings,
    ILogger<GameEngine> logger)
{
    public static readonly string[] AvailableAvatars =
        ["🐶", "🐱", "🦊", "🐻", "🐼", "🦁", "🐸", "🐵", "🐷", "🐰", "🦄", "🐨", "🐍"];

    private const int RoundEndDelaySeconds = 6;

    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly ConcurrentDictionary<string, (string RoomCode, string PlayerId)> _connections = new();

    /// <summary>Lets a client entering a room code preview which avatars are already taken, before it commits to joining.</summary>
    public List<string> GetTakenAvatars(string roomCode)
    {
        if (!_rooms.TryGetValue(roomCode.ToUpperInvariant(), out var room))
            return [];

        lock (room.Lock)
        {
            return room.Players.Where(p => p.IsConnected).Select(p => p.Avatar).ToList();
        }
    }

    public JoinResultDto CreateRoom(string connectionId, string name, string avatar)
    {
        avatar = avatar is { Length: > 0 } && AvailableAvatars.Contains(avatar) ? avatar : AvailableAvatars[0];
        name = SanitizeName(name);

        var code = RoomCodeGenerator.Generate(_rooms);
        var playerId = Guid.NewGuid().ToString("N");

        var room = new Room { Code = code, HostPlayerId = playerId };
        var player = new Player { Id = playerId, ConnectionId = connectionId, Name = name, Avatar = avatar, JoinOrder = 0 };
        room.Players.Add(player);
        room.Scores[playerId] = 0;

        _rooms[code] = room;
        _connections[connectionId] = (code, playerId);
        hubContext.Groups.AddToGroupAsync(connectionId, code);

        logger.LogInformation("Room {Code} created by {Player}", code, name);
        return new JoinResultDto(BuildRoomStateDto(room), playerId);
    }

    public JoinResultDto JoinRoom(string connectionId, string roomCode, string name, string avatar)
    {
        var room = GetRoomOrThrow(roomCode);
        lock (room.Lock)
        {
            if (room.Phase != RoomPhase.Lobby)
                throw new GameEngineException("This room has already started — you can't join mid-game.");

            var connectedCount = room.Players.Count(p => p.IsConnected);
            if (connectedCount >= 8)
                throw new GameEngineException("Room is full (max 8 players).");

            name = SanitizeName(name);

            if (!AvailableAvatars.Contains(avatar))
                throw new GameEngineException("Invalid avatar.");

            var takenAvatars = room.Players.Where(p => p.IsConnected).Select(p => p.Avatar).ToHashSet();
            if (takenAvatars.Contains(avatar))
                throw new GameEngineException("That avatar is already taken in this room.");

            var playerId = Guid.NewGuid().ToString("N");
            var nextJoinOrder = room.Players.Count == 0 ? 0 : room.Players.Max(p => p.JoinOrder) + 1;
            var player = new Player { Id = playerId, ConnectionId = connectionId, Name = name, Avatar = avatar, JoinOrder = nextJoinOrder };
            room.Players.Add(player);
            room.Scores[playerId] = 0;

            _connections[connectionId] = (room.Code, playerId);
            hubContext.Groups.AddToGroupAsync(connectionId, room.Code);

            logger.LogInformation("{Player} joined room {Code}", name, roomCode);
            BroadcastRoomState(room);
            return new JoinResultDto(BuildRoomStateDto(room), playerId);
        }
    }

    /// <summary>
    /// Re-attaches a new SignalR connection to an existing (possibly still
    /// disconnected) player record — used when a client reloads the page or
    /// briefly drops connection and wants back into the same seat, using the
    /// (roomCode, playerId) it cached client-side. Returns enough state for
    /// the client to rebuild its UI, including a round snapshot if a round
    /// is currently in progress.
    /// </summary>
    public RejoinResultDto RejoinRoom(string connectionId, string roomCode, string playerId)
    {
        var room = GetRoomOrThrow(roomCode);
        lock (room.Lock)
        {
            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player is null)
                throw new GameEngineException("That player is no longer in this room.");

            player.ConnectionId = connectionId;
            player.IsConnected = true;

            _connections[connectionId] = (room.Code, playerId);
            hubContext.Groups.AddToGroupAsync(connectionId, room.Code);

            logger.LogInformation("{Player} reconnected to room {Code}", player.Name, room.Code);
            BroadcastRoomState(room);

            RoundSnapshotDto? roundSnapshot = null;
            if (room.Phase == RoomPhase.InRound && room.CurrentRound is { } round)
            {
                var guesses = round.Guesses
                    .Select(g => new GuessResultDto(g.Word, g.IsHint ? null : g.PlayerId, g.Rank, g.Rank == 1, g.AttemptNumber, g.IsHint))
                    .ToList();

                roundSnapshot = new RoundSnapshotDto(
                    round.RoundNumber, room.Settings.RoundCount, round.SecretWord.Length,
                    round.TurnOrderPlayerIds, round.RoundDeadlineUtc, round.CurrentTurnPlayerId, round.TurnDeadlineUtc,
                    guesses, round.HintsUsedByPlayerId.Contains(playerId));
            }

            return new RejoinResultDto(BuildRoomStateDto(room), playerId, roundSnapshot);
        }
    }

    public void UpdateSettings(string connectionId, RoomSettings settings)
    {
        var (room, player) = GetRoomAndPlayerOrThrow(connectionId);
        lock (room.Lock)
        {
            if (player.Id != room.HostPlayerId)
                throw new GameEngineException("Only the host can change settings.");
            if (room.Phase != RoomPhase.Lobby)
                throw new GameEngineException("Settings can only be changed in the lobby.");

            var error = settings.Validate();
            if (error != null)
                throw new GameEngineException(error);

            room.Settings = settings;
            BroadcastRoomState(room);
        }
    }

    public void StartGame(string connectionId)
    {
        var (room, player) = GetRoomAndPlayerOrThrow(connectionId);
        lock (room.Lock)
        {
            if (player.Id != room.HostPlayerId)
                throw new GameEngineException("Only the host can start the game.");
            if (room.Phase != RoomPhase.Lobby)
                throw new GameEngineException("Game already started.");
            if (room.Players.Count(p => p.IsConnected) < 2)
                throw new GameEngineException("Need at least 2 players to start.");
            if (!embeddings.IsReady)
                throw new GameEngineException("Embeddings data isn't loaded on the server yet.");
            if (!embeddings.HasSecretWordCandidates(room.Settings.MinWordLength, room.Settings.MaxWordLength))
                throw new GameEngineException("No secret words available for that word-length range — widen it in settings.");

            room.Phase = RoomPhase.InRound;
            room.CurrentRoundNumber = 1;
            StartRound(room);
        }
    }

    public void SubmitGuess(string connectionId, string rawWord)
    {
        var (room, player) = GetRoomAndPlayerOrThrow(connectionId);
        lock (room.Lock)
        {
            var round = room.CurrentRound;
            if (room.Phase != RoomPhase.InRound || round is null || round.IsOver)
                throw new GameEngineException("No round is currently active.");

            var now = DateTime.UtcNow;
            if (round.CurrentTurnPlayerId != player.Id)
            {
                SendErrorToCaller(connectionId, "It's not your turn.");
                return;
            }
            if (now > round.TurnDeadlineUtc || now > round.RoundDeadlineUtc)
            {
                // Timer already fired or is about to; ignore late submission.
                return;
            }

            var word = (rawWord ?? string.Empty).Trim().ToLowerInvariant();
            if (word.Length == 0 || !word.All(char.IsLetter))
            {
                SendToPlayer(connectionId, "GuessRejected", new GuessRejectedDto("Enter a single word using letters only.", TurnEnded: false));
                return;
            }

            if (round.GuessedWords.Contains(word))
            {
                SendToPlayer(connectionId, "GuessRejected", new GuessRejectedDto("That word was already guessed this round.", TurnEnded: true));
                AdvanceTurn(room, round);
                return;
            }

            if (!embeddings.IsInVocabulary(word))
            {
                SendToPlayer(connectionId, "GuessRejected", new GuessRejectedDto("Unknown word — try another.", TurnEnded: false));
                return;
            }

            var rank = round.RankLookup[word];
            var attemptNumber = round.TotalAttempts + 1;
            var entry = new GuessEntry { Word = word, PlayerId = player.Id, Rank = rank, AttemptNumber = attemptNumber };
            round.Guesses.Add(entry);
            round.GuessedWords.Add(word);

            if (!round.PlayerBestGuess.TryGetValue(player.Id, out var currentBest) || rank < currentBest.Rank)
                round.PlayerBestGuess[player.Id] = entry;

            hubContext.Clients.Group(room.Code).SendAsync("GuessResult",
                new GuessResultDto(word, player.Id, rank, rank == 1, attemptNumber, IsHint: false));

            if (rank == 1)
            {
                round.WinnerPlayerId = player.Id;
                EndRound(room, round, isTimeout: false);
            }
            else
            {
                AdvanceTurn(room, round);
            }
        }
    }

    /// <summary>
    /// Reveals one not-yet-guessed word roughly halfway (by rank) toward the
    /// closest guess so far, as a nudge. Only the active player may request
    /// it, once per round, and it consumes their turn like any other guess.
    /// The hint is never attributed to the requester (or anyone) in the
    /// shared list and never counts toward any player's best-guess marker.
    /// </summary>
    public void RequestHint(string connectionId)
    {
        var (room, player) = GetRoomAndPlayerOrThrow(connectionId);
        lock (room.Lock)
        {
            var round = room.CurrentRound;
            if (room.Phase != RoomPhase.InRound || round is null || round.IsOver)
                throw new GameEngineException("No round is currently active.");

            var now = DateTime.UtcNow;
            if (round.CurrentTurnPlayerId != player.Id)
            {
                SendErrorToCaller(connectionId, "It's not your turn.");
                return;
            }
            if (now > round.TurnDeadlineUtc || now > round.RoundDeadlineUtc)
                return;

            if (round.HintsUsedByPlayerId.Contains(player.Id))
            {
                SendErrorToCaller(connectionId, "You've already used your hint this round.");
                return;
            }

            var bestRankSoFar = round.Guesses.Count > 0 ? round.Guesses.Min(g => g.Rank) : (int?)null;
            var targetRank = bestRankSoFar.HasValue ? Math.Max(2, bestRankSoFar.Value / 2) : 100;

            var candidate = round.RankLookup
                .Where(kv => kv.Value > 1 && !round.GuessedWords.Contains(kv.Key))
                .OrderBy(kv => Math.Abs(kv.Value - targetRank))
                .FirstOrDefault();

            if (candidate.Key is null)
            {
                SendErrorToCaller(connectionId, "No hint available right now.");
                return;
            }

            round.HintsUsedByPlayerId.Add(player.Id);

            var attemptNumber = round.TotalAttempts + 1;
            var entry = new GuessEntry
            {
                Word = candidate.Key, PlayerId = player.Id, Rank = candidate.Value,
                AttemptNumber = attemptNumber, IsHint = true,
            };
            round.Guesses.Add(entry);
            round.GuessedWords.Add(candidate.Key);
            // Deliberately NOT added to round.PlayerBestGuess — a hint belongs to no one.

            hubContext.Clients.Group(room.Code).SendAsync("GuessResult",
                new GuessResultDto(candidate.Key, PlayerId: null, candidate.Value, IsCorrect: false, attemptNumber, IsHint: true));

            AdvanceTurn(room, round);
        }
    }

    public void HandleDisconnect(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var info))
            return;

        if (!_rooms.TryGetValue(info.RoomCode, out var room))
            return;

        lock (room.Lock)
        {
            var player = room.Players.FirstOrDefault(p => p.Id == info.PlayerId);
            if (player is null)
                return;

            player.IsConnected = false;
            logger.LogInformation("{Player} disconnected from room {Code}", player.Name, room.Code);

            if (player.Id == room.HostPlayerId)
            {
                var nextHost = room.Players.Where(p => p.IsConnected).OrderBy(p => p.JoinOrder).FirstOrDefault();
                if (nextHost != null)
                    room.HostPlayerId = nextHost.Id;
            }

            var round = room.CurrentRound;
            if (room.Phase == RoomPhase.InRound && round is { IsOver: false } && round.CurrentTurnPlayerId == player.Id)
            {
                AdvanceTurn(room, round);
            }

            if (room.Players.All(p => !p.IsConnected))
            {
                CancelRoundTimers(room.CurrentRound);
                _rooms.TryRemove(room.Code, out _);
                logger.LogInformation("Room {Code} removed (all players disconnected)", room.Code);
                return;
            }

            BroadcastRoomState(room);
        }
    }

    // ---- Round / turn orchestration ----

    private void StartRound(Room room)
    {
        var exclude = room.UsedSecretWords;
        var secretWord = embeddings.PickSecretWord(room.Settings.MinWordLength, room.Settings.MaxWordLength, exclude);
        var rankLookup = embeddings.ComputeRankLookup(secretWord);

        var baseOrder = room.Players.OrderBy(p => p.JoinOrder).Select(p => p.Id).ToList();
        var startOffset = (room.CurrentRoundNumber - 1) % baseOrder.Count;
        var turnOrder = baseOrder.Skip(startOffset).Concat(baseOrder.Take(startOffset)).ToList();

        var now = DateTime.UtcNow;
        var round = new RoundState
        {
            RoundNumber = room.CurrentRoundNumber,
            SecretWord = secretWord,
            RankLookup = rankLookup,
            TurnOrderPlayerIds = turnOrder,
            CurrentTurnIndex = 0,
            RoundDeadlineUtc = now.AddSeconds(room.Settings.RoundSeconds),
            RoundVersion = room.CurrentRoundNumber,
        };

        room.CurrentRound = round;

        var firstConnectedIdx = FindNextConnectedIndex(room, round, startAt: 0, inclusive: true);
        if (firstConnectedIdx is null)
        {
            // Nobody connected — shouldn't happen right after StartGame, but guard anyway.
            EndRound(room, round, isTimeout: true);
            return;
        }
        round.CurrentTurnIndex = firstConnectedIdx.Value;
        round.TurnDeadlineUtc = now.AddSeconds(room.Settings.TurnSeconds);

        ScheduleRoundTimer(room, round);
        ScheduleTurnTimer(room, round);

        hubContext.Clients.Group(room.Code).SendAsync("RoundStarted", new RoundStartedDto(
            round.RoundNumber, room.Settings.RoundCount, secretWord.Length, turnOrder,
            round.RoundDeadlineUtc, round.CurrentTurnPlayerId, round.TurnDeadlineUtc));

        logger.LogInformation("Room {Code} started round {Round} (secret word length {Len})",
            room.Code, round.RoundNumber, secretWord.Length);
    }

    private void AdvanceTurn(Room room, RoundState round)
    {
        if (round.IsOver) return;

        if (DateTime.UtcNow >= round.RoundDeadlineUtc)
        {
            EndRound(room, round, isTimeout: true);
            return;
        }

        round.TurnVersion++;
        CancelTurnTimer(round);

        var nextIdx = FindNextConnectedIndex(room, round, startAt: round.CurrentTurnIndex + 1, inclusive: false);
        if (nextIdx is null)
        {
            EndRound(room, round, isTimeout: true);
            return;
        }

        round.CurrentTurnIndex = nextIdx.Value;
        round.TurnDeadlineUtc = DateTime.UtcNow.AddSeconds(room.Settings.TurnSeconds);
        ScheduleTurnTimer(room, round);

        hubContext.Clients.Group(room.Code).SendAsync("TurnChanged",
            new TurnChangedDto(round.CurrentTurnPlayerId, round.TurnDeadlineUtc, null));
    }

    private int? FindNextConnectedIndex(Room room, RoundState round, int startAt, bool inclusive)
    {
        var count = round.TurnOrderPlayerIds.Count;
        for (var offset = inclusive ? 0 : 0; offset < count; offset++)
        {
            var idx = (startAt + offset) % count;
            var playerId = round.TurnOrderPlayerIds[idx];
            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player is { IsConnected: true })
                return idx;
        }
        return null;
    }

    private void EndRound(Room room, RoundState round, bool isTimeout)
    {
        round.IsOver = true;
        CancelRoundTimers(round);

        var roundScores = new Dictionary<string, int>();
        if (!isTimeout && round.WinnerPlayerId != null)
        {
            var score = ScoringService.ComputeWinnerScore(round.TotalAttempts);
            foreach (var p in room.Players)
                roundScores[p.Id] = p.Id == round.WinnerPlayerId ? score : 0;
        }
        else
        {
            foreach (var p in room.Players)
            {
                round.PlayerBestGuess.TryGetValue(p.Id, out var best);
                roundScores[p.Id] = ScoringService.ComputeTimeoutScore(best?.Rank);
            }
        }

        foreach (var (playerId, score) in roundScores)
        {
            room.Scores.TryGetValue(playerId, out var current);
            room.Scores[playerId] = current + score;
            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player != null) player.TotalScore = room.Scores[playerId];
        }

        room.UsedSecretWords.Add(round.SecretWord);
        room.Phase = RoomPhase.RoundEnd;

        hubContext.Clients.Group(room.Code).SendAsync("RoundEnded", new RoundEndedDto(
            round.RoundNumber, round.WinnerPlayerId, round.SecretWord, round.TotalAttempts,
            roundScores, new Dictionary<string, int>(room.Scores)));

        logger.LogInformation("Room {Code} round {Round} ended (winner={Winner}, timeout={Timeout})",
            room.Code, round.RoundNumber, round.WinnerPlayerId ?? "none", isTimeout);

        if (room.CurrentRoundNumber >= room.Settings.RoundCount)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(RoundEndDelaySeconds)).ContinueWith(_ => EndGame(room));
        }
        // Otherwise the room stays in RoundEnd until the host calls StartNextRound —
        // no automatic continuation, so players have time to read the round summary.
    }

    public void StartNextRound(string connectionId)
    {
        var (room, player) = GetRoomAndPlayerOrThrow(connectionId);
        lock (room.Lock)
        {
            if (player.Id != room.HostPlayerId)
                throw new GameEngineException("Only the host can start the next round.");
            if (room.Phase != RoomPhase.RoundEnd)
                throw new GameEngineException("There's no round-end summary to advance from.");
            if (room.CurrentRoundNumber >= room.Settings.RoundCount)
                throw new GameEngineException("This was the last round.");
            if (!embeddings.HasSecretWordCandidates(room.Settings.MinWordLength, room.Settings.MaxWordLength))
                throw new GameEngineException("No secret words available for that word-length range — widen it in settings.");

            room.CurrentRoundNumber++;
            room.Phase = RoomPhase.InRound;
            StartRound(room);
        }
    }

    private void EndGame(Room room)
    {
        lock (room.Lock)
        {
            if (room.Phase == RoomPhase.GameEnd) return;
            room.Phase = RoomPhase.GameEnd;

            var winnerId = room.Scores.Count == 0
                ? null
                : room.Scores.OrderByDescending(kv => kv.Value).First().Key;

            hubContext.Clients.Group(room.Code).SendAsync("GameEnded",
                new GameEndedDto(new Dictionary<string, int>(room.Scores), winnerId));

            logger.LogInformation("Room {Code} game ended. Winner: {Winner}", room.Code, winnerId);
        }
    }

    // ---- Timers ----

    private void ScheduleTurnTimer(Room room, RoundState round)
    {
        var cts = new CancellationTokenSource();
        round.TurnTimerCts = cts;
        var turnVersion = round.TurnVersion;
        var delay = round.TurnDeadlineUtc - DateTime.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            lock (room.Lock)
            {
                if (room.CurrentRound != round || round.IsOver || round.TurnVersion != turnVersion) return;
                AdvanceTurn(room, round); // timeout on this turn = pass
            }
        }, TaskScheduler.Default);
    }

    private void ScheduleRoundTimer(Room room, RoundState round)
    {
        var cts = new CancellationTokenSource();
        round.RoundTimerCts = cts;
        var delay = round.RoundDeadlineUtc - DateTime.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            lock (room.Lock)
            {
                if (room.CurrentRound != round || round.IsOver) return;
                EndRound(room, round, isTimeout: true);
            }
        }, TaskScheduler.Default);
    }

    private static void CancelTurnTimer(RoundState round)
    {
        round.TurnTimerCts?.Cancel();
        round.TurnTimerCts = null;
    }

    private static void CancelRoundTimers(RoundState? round)
    {
        if (round is null) return;
        round.TurnTimerCts?.Cancel();
        round.TurnTimerCts = null;
        round.RoundTimerCts?.Cancel();
        round.RoundTimerCts = null;
    }

    // ---- Helpers ----

    private Room GetRoomOrThrow(string roomCode)
    {
        if (!_rooms.TryGetValue(roomCode.ToUpperInvariant(), out var room))
            throw new GameEngineException("Room not found.");
        return room;
    }

    private (Room Room, Player Player) GetRoomAndPlayerOrThrow(string connectionId)
    {
        if (!_connections.TryGetValue(connectionId, out var info))
            throw new GameEngineException("You are not in a room.");
        if (!_rooms.TryGetValue(info.RoomCode, out var room))
            throw new GameEngineException("Room not found.");
        var player = room.Players.FirstOrDefault(p => p.Id == info.PlayerId);
        if (player is null)
            throw new GameEngineException("Player not found in room.");
        return (room, player);
    }

    private static string SanitizeName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) trimmed = "Player";
        return trimmed.Length > 20 ? trimmed[..20] : trimmed;
    }

    private void BroadcastRoomState(Room room) =>
        hubContext.Clients.Group(room.Code).SendAsync("RoomState", BuildRoomStateDto(room));

    private void SendErrorToCaller(string connectionId, string message) =>
        hubContext.Clients.Client(connectionId).SendAsync("Error", message);

    private void SendToPlayer(string connectionId, string method, object payload) =>
        hubContext.Clients.Client(connectionId).SendAsync(method, payload);

    private static RoomStateDto BuildRoomStateDto(Room room) => new(
        room.Code,
        room.HostPlayerId,
        room.Phase.ToString(),
        room.Settings,
        room.Players.OrderBy(p => p.JoinOrder).Select(PlayerDto.From).ToList(),
        room.CurrentRoundNumber);
}
