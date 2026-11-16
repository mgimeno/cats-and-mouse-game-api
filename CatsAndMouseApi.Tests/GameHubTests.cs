using System.Security.Claims;
using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Hubs;
using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;
using CatsAndMouseApi.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;

namespace CatsAndMouseApi.Tests;

// Each test gets its own GameService, so tests share no state and run in parallel.
public sealed class GameHubTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingHubCallerClients _clients = new();
    private readonly GameService _games;

    public GameHubTests()
    {
        _games = new GameService(_time);
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
        var (catsHub, _, game) = await StartedHubGame();
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
    public async Task RegisterConnection_rejects_an_oversized_user_id()
    {
        var hub = CreateHub("connection-1");

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.RegisterConnection(new string('u', GameService.MaxUserIdLength + 1)));

        Assert.Equal("User id is invalid", exception.Message);
    }

    [Fact]
    public async Task RegisterConnection_during_a_game_replays_status_and_feed_to_the_new_connection_only()
    {
        var (catsHub, _, game) = await StartedHubGame();
        await catsHub.SendChatMessage(new ChatLineSentByClientModel { GameId = game.GameId, Message = "hello" });
        _clients.Clear();

        var secondTab = CreateHub("cats-connection-2");
        await secondTab.RegisterConnection("cats-user");

        var status = _clients.Messages.Single(message => message.MethodName == "GameStatus");
        Assert.Equal(["cats-connection", "cats-connection-2"], status.ConnectionIds);

        var replayedChat = _clients.SinglePayload<ChatMessage>("ChatMessage", "cats-connection-2");
        Assert.Equal("hello", replayedChat.ChatLine.Message);
        Assert.DoesNotContain(_clients.Messages, message => message.MethodName == "ChatMessage" && message.ConnectionIds.Contains("cats-connection"));

        // A second tab of a player who never left is not a reconnection worth announcing.
        Assert.DoesNotContain(_clients.Messages, message => message.MethodName == "PlayerOnlyConnectionStatusChanged");
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
    public async Task CreateGame_caps_the_user_name_without_splitting_a_surrogate_pair()
    {
        var hub = CreateHub("connection-1");
        await hub.RegisterConnection("user-1");
        var name = new string('a', GameService.MaxUserNameLength - 1) + "\U0001F42D";

        var game = await hub.CreateGame(new CreateGameModel { UserName = name, TeamId = TeamEnum.Cats });

        Assert.Equal(new string('a', GameService.MaxUserNameLength - 1), game.UserName);
    }

    [Fact]
    public async Task Game_list_shows_a_creator_their_own_user_id_and_nobody_else()
    {
        var observerHub = CreateHub("observer-connection");
        await observerHub.RegisterConnection("observer-user");
        var creatorHub = CreateHub("creator-connection");
        await creatorHub.RegisterConnection("creator-user");
        _clients.Clear();

        var created = await creatorHub.CreateGame(new CreateGameModel { UserName = "Creator", TeamId = TeamEnum.Cats });

        // The user id is the credential RegisterConnection asks for: only its owner may see it.
        Assert.Equal("creator-user", created.UserId);

        var toCreator = _clients.SinglePayload<GameListMessage>("GameList", "creator-connection");
        Assert.Equal("creator-user", Assert.Single(toCreator.GameList).UserId);

        var broadcast = _clients.Messages.Single(message => message.MethodName == "GameList" && message.IsBroadcast);
        Assert.Equal(["creator-connection"], broadcast.ConnectionIds);
        var publicItem = Assert.Single(Assert.IsType<GameListMessage>(Assert.Single(broadcast.Arguments)).GameList);
        Assert.Equal(string.Empty, publicItem.UserId);
        Assert.Equal("Creator", publicItem.UserName);

        _clients.Clear();
        await observerHub.SendGamesAwaitingForSecondPlayerToCallerAsync();
        Assert.Equal(string.Empty, Assert.Single(_clients.SinglePayload<GameListMessage>("GameList", "observer-connection").GameList).UserId);
    }

    [Fact]
    public async Task Game_status_never_carries_user_ids()
    {
        var (_, mouseHub, _) = await StartedHubGame();
        _clients.Clear();

        await mouseHub.SendInProgressGameStatusToCaller();

        // PlayerStatusModel has no user id at all: the opponent's would let either player act as the other.
        var status = _clients.SinglePayload<GameStatusMessage>("GameStatus", "mouse-connection").GameStatus;
        Assert.DoesNotContain(typeof(PlayerStatusModel).GetProperties(), property => property.Name.Contains("User", StringComparison.Ordinal) && property.Name != nameof(PlayerStatusModel.HasUserLeftTheGame));
        Assert.Equal(2, status.Players.Length);
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
        Assert.True(game.IsPasswordProtected);
    }

    [Fact]
    public async Task JoinGame_accepts_the_right_password()
    {
        var catsHub = CreateHub("cats-connection");
        var mouseHub = CreateHub("mouse-connection");
        await catsHub.RegisterConnection("cats-user");
        var game = await catsHub.CreateGame(new CreateGameModel { UserName = "Cats", TeamId = TeamEnum.Cats, GamePassword = "secret" });
        await mouseHub.RegisterConnection("mouse-user");

        await mouseHub.JoinGame(new JoinGameModel { GameId = game.GameId, UserName = "Mouse", GamePassword = "secret" });

        Assert.Contains(_clients.Messages, message => message.MethodName == "GameStart");
    }

    [Fact]
    public async Task JoinGame_rejects_joining_your_own_game()
    {
        var hub = CreateHub("connection-1");
        await hub.RegisterConnection("user-1");
        var game = await hub.CreateGame(new CreateGameModel { UserName = "Alice", TeamId = TeamEnum.Cats });

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.JoinGame(new JoinGameModel { GameId = game.GameId, UserName = "Alice" }));

        Assert.Equal("You cannot join your own game", exception.Message);
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
        Assert.Same(catsStatus.Players, mouseStatus.Players);
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
    public async Task Disconnecting_during_a_game_tells_the_opponent_and_reconnecting_tells_them_again()
    {
        var (catsHub, _, _) = await StartedHubGame();
        _clients.Clear();

        await catsHub.OnDisconnectedAsync(null);
        var disconnected = _clients.SinglePayload<PlayerOnlyConnectionStatusChangedMessage>("PlayerOnlyConnectionStatusChanged", "mouse-connection");
        Assert.False(disconnected.IsConnected);
        _clients.Clear();

        await CreateHub("cats-connection-new").RegisterConnection("cats-user");
        var reconnected = _clients.SinglePayload<PlayerOnlyConnectionStatusChangedMessage>("PlayerOnlyConnectionStatusChanged", "mouse-connection");
        Assert.True(reconnected.IsConnected);
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
    public async Task CancelGameThatHasNotStarted_removes_the_game_and_tells_everyone()
    {
        var creatorHub = CreateHub("creator-connection");
        await creatorHub.RegisterConnection("creator-user");
        var game = await creatorHub.CreateGame(new CreateGameModel { UserName = "Creator", TeamId = TeamEnum.Cats });
        _clients.Clear();

        await creatorHub.CancelGameThatHasNotStarted(new CancelGameModel { GameId = game.GameId, UserId = "creator-user" });

        Assert.Empty(_clients.LastPayload<GameListMessage>("GameList").GameList);
    }

    [Fact]
    public async Task ExitGame_tells_the_opponent_and_ends_the_game_in_their_favour()
    {
        var (catsHub, _, game) = await StartedHubGame();
        _clients.Clear();

        await catsHub.ExitGame(new GameIdModel { GameId = game.GameId });

        var left = _clients.SinglePayload<PlayerHasLeftGameMessage>("PlayerHasLeftGame", "mouse-connection");
        Assert.Equal(TeamEnum.Cats, left.TeamId);
        var mouseStatus = _clients.SinglePayload<GameStatusMessage>("GameStatus", "mouse-connection").GameStatus;
        Assert.True(mouseStatus.Players[mouseStatus.MyPlayerIndex].IsWinner);
        Assert.True(mouseStatus.Players[0].HasUserLeftTheGame);
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

    [Fact]
    public async Task Waiting_game_expires_after_its_lifetime()
    {
        var creatorHub = CreateHub("creator-connection");
        await creatorHub.RegisterConnection("creator-user");
        await creatorHub.CreateGame(new CreateGameModel { UserName = "Creator", TeamId = TeamEnum.Cats });
        var observerHub = CreateHub("observer-connection");
        await observerHub.RegisterConnection("observer-user");

        _time.Advance(GameService.WaitingGameLifetime - TimeSpan.FromSeconds(1));
        _clients.Clear();
        await observerHub.SendGamesAwaitingForSecondPlayerToCallerAsync();
        Assert.Single(_clients.SinglePayload<GameListMessage>("GameList", "observer-connection").GameList);

        _time.Advance(GameService.PruneInterval);
        _clients.Clear();
        await observerHub.SendGamesAwaitingForSecondPlayerToCallerAsync();
        Assert.Empty(_clients.SinglePayload<GameListMessage>("GameList", "observer-connection").GameList);
    }

    [Fact]
    public async Task Finished_game_expires_after_its_lifetime()
    {
        var (catsHub, mouseHub, game) = await StartedHubGame();
        await catsHub.Surrender();

        _time.Advance(GameService.FinishedGameLifetime + GameService.PruneInterval);

        var exception = await Assert.ThrowsAsync<HubException>(() => mouseHub.PlayerWantsToRematch(new GameIdModel { GameId = game.GameId }));
        Assert.Equal("Game does not exist", exception.Message);
    }

    [Fact]
    public async Task Inactive_game_in_progress_expires_but_an_active_one_does_not()
    {
        var (_, mouseHub, _) = await StartedHubGame();

        _time.Advance(GameService.InactiveGameLifetime - TimeSpan.FromMinutes(1));
        await mouseHub.Move(new MoveFigureModel { FigureId = 0, RowIndex = 6, ColumnIndex = 3 });

        _time.Advance(TimeSpan.FromMinutes(2));
        _clients.Clear();
        await mouseHub.SendWhetherHasInProgressGameToCaller();
        Assert.True(_clients.SinglePayload<PlayerHasInProgressGameMessage>("HasInProgressGame", "mouse-connection").HasInProgressGame);

        _time.Advance(GameService.InactiveGameLifetime);
        _clients.Clear();
        await mouseHub.SendWhetherHasInProgressGameToCaller();
        Assert.False(_clients.SinglePayload<PlayerHasInProgressGameMessage>("HasInProgressGame", "mouse-connection").HasInProgressGame);
    }

    [Fact]
    public async Task Storage_cap_evicts_the_least_recently_active_games_that_are_not_in_progress()
    {
        var (_, mouseHub, _) = await StartedHubGame();

        var waitingGames = new List<GameListItem>();
        for (var i = 0; i < GameService.MaxStoredGames; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(1));
            var hub = CreateHub($"connection-{i}");
            await hub.RegisterConnection($"user-{i}");
            waitingGames.Add(await hub.CreateGame(new CreateGameModel { UserName = $"User {i}", TeamId = TeamEnum.Cats }));
        }

        // 501 games against a cap of 500: the oldest waiting game went, the game in progress stayed.
        var joinerHub = CreateHub("joiner-connection");
        await joinerHub.RegisterConnection("joiner-user");
        var exception = await Assert.ThrowsAsync<HubException>(() => joinerHub.JoinGame(new JoinGameModel { GameId = waitingGames[0].GameId, UserName = "Joiner" }));
        Assert.Equal("Game does not exist", exception.Message);
        await joinerHub.JoinGame(new JoinGameModel { GameId = waitingGames[1].GameId, UserName = "Joiner" });

        _clients.Clear();
        await mouseHub.SendWhetherHasInProgressGameToCaller();
        Assert.True(_clients.SinglePayload<PlayerHasInProgressGameMessage>("HasInProgressGame", "mouse-connection").HasInProgressGame);
    }

    [Fact]
    public async Task Game_list_holds_the_newest_games_up_to_its_cap()
    {
        for (var i = 0; i < GameService.MaxListedGames + 10; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(1));
            var hub = CreateHub($"connection-{i}");
            await hub.RegisterConnection($"user-{i}");
            await hub.CreateGame(new CreateGameModel { UserName = $"User {i}", TeamId = TeamEnum.Cats });
        }

        _clients.Clear();
        await CreateHub("observer-connection").RegisterConnection("observer-user");
        var gameList = _clients.SinglePayload<GameListMessage>("GameList", "observer-connection").GameList;

        Assert.Equal(GameService.MaxListedGames, gameList.Length);
        Assert.Equal($"User {GameService.MaxListedGames + 9}", gameList[0].UserName);
        Assert.Equal("User 10", gameList[^1].UserName);
    }

    private GameHub CreateHub(string connectionId)
    {
        return new GameHub(_games)
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
    public List<SentClientMessage> Messages { get; } = [];

    public IClientProxy All => new RecordingClientProxy(Messages, [], isBroadcast: true);
    public IClientProxy Caller => throw new NotSupportedException();
    public IClientProxy Others => throw new NotSupportedException();

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy(Messages, excludedConnectionIds, isBroadcast: true);
    public IClientProxy Client(string connectionId) => new RecordingClientProxy(Messages, [connectionId], isBroadcast: false);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new RecordingClientProxy(Messages, connectionIds, isBroadcast: false);
    public IClientProxy Group(string groupName) => throw new NotSupportedException();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
    public IClientProxy OthersInGroup(string groupName) => throw new NotSupportedException();
    public IClientProxy User(string userId) => throw new NotSupportedException();
    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

    public void Clear()
    {
        Messages.Clear();
    }

    /// <summary>The one message sent to exactly that connection by name (broadcasts excluded).</summary>
    public T SinglePayload<T>(string methodName, string connectionId)
    {
        var message = Messages.Single(message =>
            message.MethodName == methodName &&
            !message.IsBroadcast &&
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
    IReadOnlyList<string> connectionIds,
    bool isBroadcast) : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        messages.Add(new SentClientMessage(method, [.. connectionIds], args, isBroadcast));
        return Task.CompletedTask;
    }
}

/// <param name="ConnectionIds">The recipients, or for a broadcast the connections it excludes.</param>
internal sealed record SentClientMessage(
    string MethodName,
    IReadOnlyList<string> ConnectionIds,
    IReadOnlyList<object?> Arguments,
    bool IsBroadcast);

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
