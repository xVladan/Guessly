using Guessly.Api.Dtos;
using Guessly.Api.Models;
using Guessly.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Guessly.Api.Hubs;

/// <summary>
/// Thin real-time adapter: every method just delegates into GameEngine, which
/// owns all room state and business rules. Client-facing errors are surfaced
/// as HubException so the JS client's promise rejects with a readable message.
/// </summary>
public sealed class GameHub(GameEngine engine) : Hub
{
    public JoinResultDto CreateRoom(string name, string avatar)
    {
        try
        {
            return engine.CreateRoom(Context.ConnectionId, name, avatar);
        }
        catch (GameEngineException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public JoinResultDto JoinRoom(string roomCode, string name, string avatar)
    {
        try
        {
            return engine.JoinRoom(Context.ConnectionId, roomCode, name, avatar);
        }
        catch (GameEngineException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public void UpdateSettings(RoomSettings settings)
    {
        try
        {
            engine.UpdateSettings(Context.ConnectionId, settings);
        }
        catch (GameEngineException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public void StartGame()
    {
        try
        {
            engine.StartGame(Context.ConnectionId);
        }
        catch (GameEngineException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public void SubmitGuess(string word)
    {
        try
        {
            engine.SubmitGuess(Context.ConnectionId, word);
        }
        catch (GameEngineException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public void StartNextRound()
    {
        try
        {
            engine.StartNextRound(Context.ConnectionId);
        }
        catch (GameEngineException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public void RequestHint()
    {
        try
        {
            engine.RequestHint(Context.ConnectionId);
        }
        catch (GameEngineException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        engine.HandleDisconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
