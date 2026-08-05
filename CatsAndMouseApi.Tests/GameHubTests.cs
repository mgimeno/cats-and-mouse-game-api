using System.Collections;
using System.Reflection;
using System.Security.Claims;
using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Hubs;
using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace CatsAndMouseApi.Tests;

[CollectionDefinition(nameof(GameHubTestCollection), DisableParallelization = true)]
public sealed class GameHubTestCollection;

[Collection(nameof(GameHubTestCollection))]
public sealed class GameHubTests : IDisposable
{
    private readonly RecordingHubCallerClients _clients = new();

    public GameHubTests()
    {
        GameHubState.Reset();
    }

    public void Dispose()
    {
        GameHubState.Reset();
    }

    [Fact]
    public async Task Ping_does_not_require_a_registered_connection()
    {
        var hub = CreateHub("connection-1");

        await hub.Ping();

        Assert.Empty(_clients.Messages);
    }

    [Fact]
    public async Task Ping_sends_nothing_while_a_game_is_in_progress()
    {
        var catsHub = CreateHub("cats-connection");
        var mouseHub = CreateHub("mouse-connection");
        await catsHub.RegisterConnection("cats-user");
        var game = await catsHub.CreateGame(new CreateGameModel { UserName = "Cats", TeamId = TeamEnum.Cats });
        await mouseHub.RegisterConnection("mouse-user");
        await mouseHub.JoinGame(new JoinGameModel { GameId = game.GameId, UserName = "Mouse" });
        await catsHub.SendChatMessage(new ChatLineSentByClientModel { GameId = game.GameId, Message = "hello" });
        _clients.Clear();

        await catsHub.Ping();

        // Clients probe with this on every return to the foreground. Replaying the game
        // state and chat history here would duplicate every chat line in their UI, and
        // announcing a reconnection would spam the opponent on each tab switch.
        Assert.Empty(_clients.Messages);
    }

    [Fact]
    public async Task RegisterConnection_without_game_sends_empty_list_and_no_in_progress_game()
    {
        var hub = CreateHub("connection-1");

        await hub.RegisterConnection("user-1");

        var gameList = _clients.SinglePayload<GameListMessage>("GameList", "connection-1");
        var hasInProgressGame = _clients.SinglePayload<PlayerHasInProgressGameMessage>("HasInProgressGame", "connection-1");

        Assert.Empty(gameList.GameList);
        Assert.False(hasInProgressGame.HasInProgressGame);
    }

    [Fact]
    public async Task SendInProgressGameStatusToCaller_without_game_sends_no_in_progress_game()
    {
        var hub = CreateHub("connection-1");
        await hub.RegisterConnection("user-1");
        _clients.Clear();

        await hub.SendInProgressGameStatusToCaller();

        var hasInProgressGame = _clients.SinglePayload<PlayerHasInProgressGameMessage>("HasInProgressGame", "connection-1");
        Assert.False(hasInProgressGame.HasInProgressGame);
        Assert.DoesNotContain(_clients.Messages, message => message.MethodName == "GameStatus");
    }

    [Fact]
    public async Task CreateGame_requires_registered_connection()
    {
        var hub = CreateHub("connection-1");

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.CreateGame(new CreateGameModel
        {
            UserName = "Alice",
            TeamId = TeamEnum.Cats
        }));

        Assert.Equal("Connection is not registered", exception.Message);
    }

    [Fact]
    public async Task CreateGame_rejects_duplicate_waiting_game_for_same_user()
    {
        var hub = CreateHub("connection-1");
        await hub.RegisterConnection("user-1");
        await hub.CreateGame(new CreateGameModel { UserName = "Alice", TeamId = TeamEnum.Cats });

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.CreateGame(new CreateGameModel
        {
            UserName = "Alice",
            TeamId = TeamEnum.Mouse
        }));

        Assert.Equal("You are already creating another game", exception.Message);
    }

    [Fact]
    public async Task JoinGame_starts_game_and_sends_status_to_both_players()
    {
        var catsHub = CreateHub("cats-connection");
        var mouseHub = CreateHub("mouse-connection");
        await catsHub.RegisterConnection("cats-user");
        var game = await catsHub.CreateGame(new CreateGameModel { UserName = "Cats", TeamId = TeamEnum.Cats });
        await mouseHub.RegisterConnection("mouse-user");
        _clients.Clear();

        await mouseHub.JoinGame(new JoinGameModel { GameId = game.GameId, UserName = "Mouse" });

        Assert.Contains(_clients.Messages, message =>
            message.MethodName == "GameStart" &&
            message.ConnectionIds.Contains("cats-connection") &&
            message.ConnectionIds.Contains("mouse-connection"));

        var catsStatus = _clients.SinglePayload<GameStatusMessage>("GameStatus", "cats-connection").GameStatus;
        var mouseStatus = _clients.SinglePayload<GameStatusMessage>("GameStatus", "mouse-connection").GameStatus;

        Assert.Equal(0, catsStatus.MyPlayerIndex);
        Assert.Equal(1, mouseStatus.MyPlayerIndex);
        Assert.False(catsStatus.Players[0].IsTheirTurn);
        Assert.True(mouseStatus.Players[1].IsTheirTurn);
        Assert.Empty(_clients.LastPayload<GameListMessage>("GameList").GameList);
    }

    [Fact]
    public async Task JoinGame_rejects_invalid_password()
    {
        var catsHub = CreateHub("cats-connection");
        var mouseHub = CreateHub("mouse-connection");
        await catsHub.RegisterConnection("cats-user");
        var game = await catsHub.CreateGame(new CreateGameModel
        {
            UserName = "Cats",
            TeamId = TeamEnum.Cats,
            GamePassword = "secret"
        });
        await mouseHub.RegisterConnection("mouse-user");

        var exception = await Assert.ThrowsAsync<HubException>(() => mouseHub.JoinGame(new JoinGameModel
        {
            GameId = game.GameId,
            UserName = "Mouse",
            GamePassword = "wrong"
        }));

        Assert.Equal("Game password is invalid", exception.Message);
    }

    [Fact]
    public async Task Move_rejects_out_of_turn_and_invalid_destinations()
    {
        var (catsHub, mouseHub, _) = await StartedHubGame();

        var outOfTurnException = await Assert.ThrowsAsync<HubException>(() => catsHub.Move(new MoveFigureModel
        {
            FigureId = 1,
            RowIndex = 1,
            ColumnIndex = 0
        }));
        Assert.Equal("It's not your turn", outOfTurnException.Message);

        var invalidMoveException = await Assert.ThrowsAsync<HubException>(() => mouseHub.Move(new MoveFigureModel
        {
            FigureId = 0,
            RowIndex = 0,
            ColumnIndex = 0
        }));
        Assert.Equal("This figure cannot be moved to that position", invalidMoveException.Message);
    }

    [Fact]
    public async Task Move_valid_move_advances_turn_and_sends_status()
    {
        var (_, mouseHub, _) = await StartedHubGame();
        _clients.Clear();

        await mouseHub.Move(new MoveFigureModel
        {
            FigureId = 0,
            RowIndex = 6,
            ColumnIndex = 3
        });

        var catsStatus = _clients.SinglePayload<GameStatusMessage>("GameStatus", "cats-connection").GameStatus;
        var mouseStatus = _clients.SinglePayload<GameStatusMessage>("GameStatus", "mouse-connection").GameStatus;

        Assert.True(catsStatus.Players[0].IsTheirTurn);
        Assert.False(mouseStatus.Players[1].IsTheirTurn);
        Assert.Equal(6, mouseStatus.Players[1].Figures[0].Position.RowIndex);
        Assert.Equal(3, mouseStatus.Players[1].Figures[0].Position.ColumnIndex);
    }

    [Fact]
    public async Task SendChatMessage_trims_whitespace_and_caps_payload_length()
    {
        var (_, mouseHub, game) = await StartedHubGame();
        _clients.Clear();

        await mouseHub.SendChatMessage(new ChatLineSentByClientModel
        {
            GameId = game.GameId,
            Message = $"  {new string('x', 1_200)}  "
        });

        var chat = _clients.SinglePayload<ChatMessage>("ChatMessage", "cats-connection");

        Assert.Equal(1_000, chat.ChatLine.Message.Length);
        Assert.False(chat.ChatLine.Message.StartsWith(' '));
        Assert.Equal(TeamEnum.Mouse, chat.ChatLine.TeamId);
    }

    [Fact]
    public async Task Disconnecting_last_connection_cancels_waiting_game()
    {
        var creatorHub = CreateHub("creator-connection");
        await creatorHub.RegisterConnection("creator-user");
        await creatorHub.CreateGame(new CreateGameModel { UserName = "Creator", TeamId = TeamEnum.Cats });

        await creatorHub.OnDisconnectedAsync(null);
        var observerHub = CreateHub("observer-connection");

        await observerHub.RegisterConnection("observer-user");

        var gameList = _clients.LastPayload<GameListMessage>("GameList");
        Assert.Empty(gameList.GameList);
    }

    [Fact]
    public async Task CancelGameThatHasNotStarted_rejects_another_users_game()
    {
        var creatorHub = CreateHub("creator-connection");
        var otherHub = CreateHub("other-connection");
        await creatorHub.RegisterConnection("creator-user");
        var game = await creatorHub.CreateGame(new CreateGameModel { UserName = "Creator", TeamId = TeamEnum.Cats });
        await otherHub.RegisterConnection("other-user");

        var exception = await Assert.ThrowsAsync<HubException>(() => otherHub.CancelGameThatHasNotStarted(new CancelGameModel
        {
            GameId = game.GameId,
            UserId = "creator-user"
        }));

        Assert.Equal("You cannot cancel another player's game", exception.Message);
    }

    [Fact]
    public async Task PlayerWantsToRematch_when_both_players_agree_starts_new_game_with_swapped_teams()
    {
        var (catsHub, mouseHub, game) = await StartedHubGame();
        await catsHub.Surrender();
        _clients.Clear();

        await catsHub.PlayerWantsToRematch(new GameIdModel { GameId = game.GameId });
        await mouseHub.PlayerWantsToRematch(new GameIdModel { GameId = game.GameId });

        var catsUserStatus = _clients.SinglePayload<GameStatusMessage>("GameStatus", "cats-connection").GameStatus;
        var mouseUserStatus = _clients.SinglePayload<GameStatusMessage>("GameStatus", "mouse-connection").GameStatus;
        var catsUserPlayer = catsUserStatus.Players[catsUserStatus.MyPlayerIndex];
        var mouseUserPlayer = mouseUserStatus.Players[mouseUserStatus.MyPlayerIndex];

        Assert.NotEqual(game.GameId, catsUserStatus.GameId);
        Assert.Equal(catsUserStatus.GameId, mouseUserStatus.GameId);
        Assert.Equal(TeamEnum.Mouse, catsUserPlayer.TeamId);
        Assert.Equal(TeamEnum.Cats, mouseUserPlayer.TeamId);
        Assert.True(catsUserPlayer.IsTheirTurn);
        Assert.False(mouseUserPlayer.IsTheirTurn);
    }

    private GameHub CreateHub(string connectionId)
    {
        return new GameHub
        {
            Clients = _clients,
            Context = new TestHubCallerContext(connectionId)
        };
    }

    private async Task<(GameHub CatsHub, GameHub MouseHub, GameListItem Game)> StartedHubGame()
    {
        var catsHub = CreateHub("cats-connection");
        var mouseHub = CreateHub("mouse-connection");

        await catsHub.RegisterConnection("cats-user");
        var game = await catsHub.CreateGame(new CreateGameModel { UserName = "Cats", TeamId = TeamEnum.Cats });
        await mouseHub.RegisterConnection("mouse-user");
        await mouseHub.JoinGame(new JoinGameModel { GameId = game.GameId, UserName = "Mouse" });

        return (catsHub, mouseHub, game);
    }
}

internal sealed class RecordingHubCallerClients : IHubCallerClients
{
    private readonly RecordingClientProxy _allProxy;

    public RecordingHubCallerClients()
    {
        _allProxy = new RecordingClientProxy(Messages, []);
    }

    public List<SentClientMessage> Messages { get; } = [];

    public IClientProxy All => _allProxy;
    public IClientProxy Caller => _allProxy;
    public IClientProxy Others => _allProxy;

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _allProxy;
    public IClientProxy Client(string connectionId) => new RecordingClientProxy(Messages, [connectionId]);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new RecordingClientProxy(Messages, connectionIds);
    public IClientProxy Group(string groupName) => _allProxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _allProxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => _allProxy;
    public IClientProxy OthersInGroup(string groupName) => _allProxy;
    public IClientProxy User(string userId) => _allProxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => _allProxy;

    public void Clear()
    {
        Messages.Clear();
    }

    public T SinglePayload<T>(string methodName, string connectionId)
    {
        var message = Messages.Single(message =>
            message.MethodName == methodName &&
            message.ConnectionIds.Contains(connectionId));

        return Assert.IsType<T>(Assert.Single(message.Arguments));
    }

    public T LastPayload<T>(string methodName)
    {
        var message = Messages.Last(message => message.MethodName == methodName);
        return Assert.IsType<T>(Assert.Single(message.Arguments));
    }
}

internal sealed class RecordingClientProxy(
    List<SentClientMessage> messages,
    IReadOnlyList<string> connectionIds) : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        messages.Add(new SentClientMessage(method, [.. connectionIds], args));
        return Task.CompletedTask;
    }
}

internal sealed record SentClientMessage(
    string MethodName,
    IReadOnlyList<string> ConnectionIds,
    IReadOnlyList<object?> Arguments);

internal sealed class TestHubCallerContext(string connectionId) : HubCallerContext
{
    public override string ConnectionId { get; } = connectionId;
    public override string? UserIdentifier => null;
    public override ClaimsPrincipal User { get; } = new(new ClaimsIdentity());
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort()
    {
    }
}

internal static class GameHubState
{
    public static void Reset()
    {
        var games = (IList)GetRequiredField(typeof(GameHub), "_games").GetValue(null)!;
        games.Clear();

        var connections = GetRequiredField(typeof(GameHub), "_connections").GetValue(null)!;
        ((IDictionary)GetRequiredField(connections.GetType(), "_connections").GetValue(connections)!).Clear();
        ((IDictionary)GetRequiredField(connections.GetType(), "_connectionToKey").GetValue(connections)!).Clear();
    }

    private static FieldInfo GetRequiredField(Type type, string name)
    {
        return type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field {name} was not found on {type.Name}.");
    }
}
