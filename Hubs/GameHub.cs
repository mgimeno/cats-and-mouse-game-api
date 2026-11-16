using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;
using CatsAndMouseApi.Services;
using Microsoft.AspNetCore.SignalR;

namespace CatsAndMouseApi.Hubs;

/// <summary>
/// The SignalR surface the UI calls. Method names and parameter shapes are the client contract.
/// All state and rules live in <see cref="GameService"/>; each method hands the caller's
/// connection to it and delivers the messages it returns.
/// </summary>
public sealed class GameHub(GameService games) : Hub
{
    /// <summary>
    /// Liveness probe for clients returning from the background. A page that was
    /// frozen can come back holding a socket the OS already tore down, which still
    /// reports itself as connected; only a completed round trip proves otherwise.
    /// </summary>
    /// <remarks>
    /// Must stay free of side effects and must not push any message. Callers invoke
    /// it on every return to the foreground, so anything done here runs constantly.
    /// </remarks>
    public Task Ping() => Task.CompletedTask;

    public Task RegisterConnection(string userId)
    {
        return SendAsync(games.RegisterConnection(Context.ConnectionId, userId));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await SendAsync(games.Disconnect(Context.ConnectionId));
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<GameListItem> CreateGame(CreateGameModel model)
    {
        var (game, messages) = games.CreateGame(Context.ConnectionId, model);
        await SendAsync(messages);
        return game;
    }

    public Task JoinGame(JoinGameModel model)
    {
        return SendAsync(games.JoinGame(Context.ConnectionId, model));
    }

    public Task Move(MoveFigureModel model)
    {
        return SendAsync(games.Move(Context.ConnectionId, model));
    }

    public Task CancelGameThatHasNotStarted(CancelGameModel model)
    {
        return SendAsync(games.CancelGameThatHasNotStarted(Context.ConnectionId, model));
    }

    public Task SendInProgressGameStatusToCaller()
    {
        return SendAsync(games.GetInProgressGameStatus(Context.ConnectionId));
    }

    public Task SendWhetherHasInProgressGameToCaller()
    {
        return SendAsync(games.GetHasInProgressGame(Context.ConnectionId));
    }

    public Task SendChatMessage(ChatLineSentByClientModel model)
    {
        return SendAsync(games.SendChatMessage(Context.ConnectionId, model));
    }

    public Task ExitGame(GameIdModel model)
    {
        return SendAsync(games.ExitGame(Context.ConnectionId, model));
    }

    public Task Surrender()
    {
        return SendAsync(games.Surrender(Context.ConnectionId));
    }

    public Task PlayerWantsToRematch(GameIdModel model)
    {
        return SendAsync(games.PlayerWantsToRematch(Context.ConnectionId, model));
    }

    // The client calls it by this exact name, "Async" included.
    public Task SendGamesAwaitingForSecondPlayerToCallerAsync()
    {
        return SendAsync(games.GetGameList(Context.ConnectionId));
    }

    /// <summary>
    /// One at a time and in order: a client has to see GameStart before the GameStatus that
    /// follows it, and chat lines in the order they were said. Each send normally completes
    /// synchronously, straight into the connection's output pipe.
    /// </summary>
    private async Task SendAsync(IReadOnlyList<ClientMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.Audience == ClientMessageAudience.AllExcept)
            {
                await Clients.AllExcept(message.ConnectionIds).SendAsync(message.MethodName, message.Message);
            }
            else if (message.ConnectionIds.Count > 0)
            {
                await Clients.Clients(message.ConnectionIds).SendAsync(message.MethodName, message.Message);
            }
        }
    }
}
