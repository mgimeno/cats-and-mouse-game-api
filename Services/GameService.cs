using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Hubs;
using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;
using Microsoft.AspNetCore.SignalR;

namespace CatsAndMouseApi.Services;

/// <summary>
/// Every game and every registered connection, in memory. A singleton: hubs are created per
/// invocation, so the state lives here and the hub stays a thin transport. Each operation runs
/// under one lock and returns the messages it produced; the hub sends them after the lock is
/// released, so no client I/O ever happens while the lock is held. A rule violation is a
/// <see cref="HubException"/>, whose message the caller receives.
/// </summary>
public sealed class GameService
{
    public const int MaxChatMessageLength = 1000;
    public const int MaxStoredGames = 500;

    // The game list goes to every connected client whenever it changes, and each creator gets a
    // copy of their own. Capping it bounds that work at a few thousand items per change however
    // many games are waiting, where all 500 would cost hundreds of thousands. A hundred is far
    // more than anyone scrolls through to pick an opponent.
    public const int MaxListedGames = 100;

    // The UI caps names at 10 characters. The server cannot trust that, and a name is broadcast to
    // every connected client in the game list, so it gets a generous cap of its own.
    public const int MaxUserNameLength = 32;

    // The UI sends a 36-character UUID. Every registered id is held in memory for as long as its
    // connection lives, so anything far longer is not a real client.
    public const int MaxUserIdLength = 64;

    public static readonly TimeSpan WaitingGameLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan FinishedGameLifetime = TimeSpan.FromHours(2);
    public static readonly TimeSpan InactiveGameLifetime = TimeSpan.FromHours(12);

    // Lifetimes are minutes to hours long, so expiring games on every call bought nothing but a
    // walk over every game per message. Every 15 seconds keeps expiry exact to well within that.
    public static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(15);

    private const int GameIdLength = 5;

    private readonly TimeProvider _timeProvider;
    private readonly long _pruneIntervalTicks;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, GameModel> _games = [with(StringComparer.Ordinal)];
    private readonly ConnectionMapping<string> _connections = new();
    private long _nextPruneTimestamp;

    public GameService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _pruneIntervalTicks = (long)(PruneInterval.TotalSeconds * timeProvider.TimestampFrequency);
    }

    public IReadOnlyList<ClientMessage> RegisterConnection(string connectionId, string? userId)
    {
        userId = NormalizeUserId(userId);
        List<ClientMessage> messages = [];

        lock (_lock)
        {
            // Registering has to happen under the same lock that guards the feed read below.
            // Outside it, a message sent in between would be broadcast to this brand new connection
            // *and* appear in the replay it is about to receive, showing up twice in the chat.
            var connectionCount = _connections.Add(userId, connectionId);
            PruneExpiredGamesIfDue();

            var userConnections = _connections.GetConnectionsByKey(userId);
            var game = FindInProgressGameForUser(userId);
            if (game is not null)
            {
                var player = GetRequiredPlayer(game, userId);
                messages.Add(BuildGameStatusMessage(game, player, userConnections));
                AddFeedReplay(messages, game, connectionId);

                if (connectionCount == 1)
                {
                    AddConnectionStatusChanged(messages, game, player, isConnected: true);
                }
            }
            else
            {
                messages.Add(BuildGameListMessageForUser(userId, userConnections));
            }

            messages.Add(BuildHasInProgressGameMessage(userConnections, game is not null));
        }

        return messages;
    }

    public IReadOnlyList<ClientMessage> Disconnect(string connectionId)
    {
        lock (_lock)
        {
            var result = _connections.RemoveConnection(connectionId);
            if (result is null || result.HasOtherActiveConnections)
            {
                return [];
            }

            PruneExpiredGamesIfDue();

            List<ClientMessage> messages = [];
            var userId = result.Key;
            var game = FindInProgressGameForUser(userId);

            if (CancelWaitingGamesCreatedBy(userId))
            {
                AddGameListBroadcast(messages);
            }

            if (game?.GetPlayerByUserId(userId) is { } player)
            {
                AddConnectionStatusChanged(messages, game, player, isConnected: false);
            }

            return messages;
        }
    }

    public (GameListItem Game, IReadOnlyList<ClientMessage> Messages) CreateGame(string connectionId, CreateGameModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<ClientMessage> messages = [];

        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            var userName = NormalizeUserName(model.UserName);
            ValidateTeam(model.TeamId);
            PruneExpiredGamesIfDue();

            if (HasWaitingGameCreatedBy(userId))
            {
                throw new HubException("You are already creating another game");
            }

            var game = AddNewGame(model.GamePassword);
            game.SetFirstPlayer(model.TeamId, userName, userId);
            EvictExcessGames();

            AddGameListBroadcast(messages);
            return (BuildGameListItem(game, includeUserId: true), messages);
        }
    }

    public IReadOnlyList<ClientMessage> JoinGame(string connectionId, JoinGameModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<ClientMessage> messages = [];

        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            var userName = NormalizeUserName(model.UserName);
            var gameId = NormalizeRequired(model.GameId, "Game id is required");
            PruneExpiredGamesIfDue();

            if (!_games.TryGetValue(gameId, out var game))
            {
                throw new HubException("Game does not exist");
            }

            if (game.IsPasswordProtected() && !PasswordMatches(game.Password!, model.GamePassword))
            {
                throw new HubException("Game password is invalid");
            }

            if (!game.IsWaitingForSecondPlayer())
            {
                throw new HubException("Game is in progress or over");
            }

            if (string.Equals(game.Players[0].UserId, userId, StringComparison.Ordinal))
            {
                throw new HubException("You cannot join your own game");
            }

            game.SetSecondPlayer(userName, userId);
            game.Start();

            messages.Add(ClientMessage.To(GetConnectionsOfPlayers(game), ClientMethodNames.GameStart, GameStartMessage.Instance));
            AddGameStatusForAllPlayers(messages, game);
            AddGameListBroadcast(messages);
        }

        return messages;
    }

    public IReadOnlyList<ClientMessage> Move(string connectionId, MoveFigureModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<ClientMessage> messages = [];

        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            var game = GetRequiredInProgressGame(userId);
            if (game.IsGameOver())
            {
                throw new HubException("Game is over");
            }

            var player = GetRequiredPlayer(game, userId);
            if (!player.IsTheirTurn)
            {
                throw new HubException("It's not your turn");
            }

            var figure = GameModel.GetPlayerFigure(player, model.FigureId) ?? throw new HubException("Figure does not exist");
            if (!figure.CanMoveTo(model.RowIndex, model.ColumnIndex))
            {
                throw new HubException("This figure cannot be moved to that position");
            }

            game.Move(figure, model.RowIndex, model.ColumnIndex);
            if (!game.IsGameOver())
            {
                game.SetNextTurn();
            }

            AddGameStatusForAllPlayers(messages, game);
        }

        return messages;
    }

    public IReadOnlyList<ClientMessage> CancelGameThatHasNotStarted(string connectionId, CancelGameModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (_lock)
        {
            var currentUserId = GetRequiredUserId(connectionId);
            var modelUserId = NormalizeRequired(model.UserId, "User id is required");
            if (!string.Equals(currentUserId, modelUserId, StringComparison.Ordinal))
            {
                throw new HubException("You cannot cancel another player's game");
            }

            var gameId = NormalizeRequired(model.GameId, "Game id is required");
            PruneExpiredGamesIfDue();

            if (!CancelWaitingGame(gameId, currentUserId) || !model.SendAwaitingGamesToAllClients)
            {
                return [];
            }

            List<ClientMessage> messages = [];
            AddGameListBroadcast(messages);
            return messages;
        }
    }

    /// <summary>The caller's game as they see it, or that they have none.</summary>
    public IReadOnlyList<ClientMessage> GetInProgressGameStatus(string connectionId)
    {
        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            var userConnections = _connections.GetConnectionsByKey(userId);
            var game = FindInProgressGameForUser(userId);
            return game is null
                ? [BuildHasInProgressGameMessage(userConnections, hasInProgressGame: false)]
                : [BuildGameStatusMessage(game, GetRequiredPlayer(game, userId), userConnections)];
        }
    }

    public IReadOnlyList<ClientMessage> GetHasInProgressGame(string connectionId)
    {
        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            var hasInProgressGame = FindInProgressGameForUser(userId) is not null;
            return [BuildHasInProgressGameMessage(_connections.GetConnectionsByKey(userId), hasInProgressGame)];
        }
    }

    public IReadOnlyList<ClientMessage> GetGameList(string connectionId)
    {
        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            return [BuildGameListMessageForUser(userId, _connections.GetConnectionsByKey(userId))];
        }
    }

    public IReadOnlyList<ClientMessage> SendChatMessage(string connectionId, ChatLineSentByClientModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var messageText = Truncate(NormalizeRequired(model.Message, "Message is required"), MaxChatMessageLength);

        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            var game = GetRequiredGameForUser(model.GameId, userId);
            var player = GetRequiredPlayer(game, userId);

            var message = new ChatMessage(game.Id, new ChatLineModel(player.Name, player.TeamId, messageText));
            game.AddToFeed(message);

            return [ClientMessage.To(GetConnectionsOfPlayers(game), ClientMethodNames.ChatMessage, message)];
        }
    }

    public IReadOnlyList<ClientMessage> ExitGame(string connectionId, GameIdModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<ClientMessage> messages = [];

        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            var game = GetRequiredGameForUser(model.GameId, userId);
            var playerWhoLeft = GetRequiredPlayer(game, userId);
            var opponentPlayer = game.GetOpponentPlayer(playerWhoLeft) ?? throw new HubException("Opponent does not exist");

            var message = new PlayerHasLeftGameMessage(game.Id, playerWhoLeft.Name, playerWhoLeft.TeamId);
            game.AddToFeed(message);

            var opponentConnections = _connections.GetConnectionsByKey(opponentPlayer.UserId);
            messages.Add(ClientMessage.To(opponentConnections, ClientMethodNames.PlayerHasLeftGame, message));

            game.PlayerLeft(playerWhoLeft);

            if (!opponentPlayer.HasUserLeftTheGame)
            {
                messages.Add(BuildGameStatusMessage(game, opponentPlayer, opponentConnections));
            }
        }

        return messages;
    }

    public IReadOnlyList<ClientMessage> Surrender(string connectionId)
    {
        List<ClientMessage> messages = [];

        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            var game = GetRequiredInProgressGame(userId);
            var playerWhoSurrenders = GetRequiredPlayer(game, userId);

            var message = new PlayerHasSurrenderedMessage(game.Id, playerWhoSurrenders.Name, playerWhoSurrenders.TeamId);
            game.AddToFeed(message);
            messages.Add(ClientMessage.To(GetConnectionsOfPlayers(game), ClientMethodNames.PlayerHasSurrendered, message));

            game.PlayerSurrenders(playerWhoSurrenders);
            AddGameStatusForAllPlayers(messages, game);
        }

        return messages;
    }

    public IReadOnlyList<ClientMessage> PlayerWantsToRematch(string connectionId, GameIdModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<ClientMessage> messages = [];

        lock (_lock)
        {
            var userId = GetRequiredUserId(connectionId);
            PruneExpiredGamesIfDue();

            var game = GetRequiredGameForUser(model.GameId, userId);
            if (!game.IsGameOver())
            {
                throw new HubException("Game is not over");
            }

            if (game.HasAnyPlayerLeft())
            {
                throw new HubException("Opponent has left");
            }

            var playerWhoWantsToRematch = GetRequiredPlayer(game, userId);
            var opponentPlayer = game.GetOpponentPlayer(playerWhoWantsToRematch) ?? throw new HubException("Opponent does not exist");

            if (!playerWhoWantsToRematch.WantsToRematch)
            {
                game.PlayerWantsToRematch(playerWhoWantsToRematch);

                var message = new PlayerWantsRematchMessage(game.Id, playerWhoWantsToRematch.Name, playerWhoWantsToRematch.TeamId);
                game.AddToFeed(message);
                messages.Add(ClientMessage.To(GetConnectionsOfPlayers(game), ClientMethodNames.PlayerWantsRematch, message));
            }

            if (game.IsReadyForRematch() && game.RematchGameId is null)
            {
                // Teams swap on a rematch, and the mouse moves first as always.
                var rematchGame = AddNewGame(gamePassword: null);
                rematchGame.SetFirstPlayer(GetOppositeTeam(playerWhoWantsToRematch.TeamId), playerWhoWantsToRematch.Name, playerWhoWantsToRematch.UserId);
                rematchGame.SetSecondPlayer(opponentPlayer.Name, opponentPlayer.UserId);
                rematchGame.Start();
                game.RematchGameId = rematchGame.Id;
                EvictExcessGames();

                AddGameStatusForAllPlayers(messages, rematchGame);
            }
        }

        return messages;
    }

    private GameModel AddNewGame(string? gamePassword)
    {
        string gameId;
        do
        {
            gameId = RandomNumberGenerator.GetHexString(GameIdLength, lowercase: true);
        }
        while (_games.ContainsKey(gameId));

        var game = new GameModel(gameId, gamePassword, _timeProvider);
        _games.Add(gameId, game);
        return game;
    }

    /// <summary>
    /// The user's game in progress. A user can end up in two (by joining a game while their own
    /// waits for an opponent, which then starts too), so the most recently started one wins: it is
    /// the one they are looking at.
    /// </summary>
    private GameModel? FindInProgressGameForUser(string userId)
    {
        GameModel? result = null;
        foreach (var game in _games.Values)
        {
            if (game.IsGameInProgress() &&
                game.GetPlayerByUserId(userId) is not null &&
                (result is null || game.StartedTimestamp > result.StartedTimestamp))
            {
                result = game;
            }
        }

        return result;
    }

    private GameModel GetRequiredInProgressGame(string userId)
    {
        return FindInProgressGameForUser(userId) ?? throw new HubException("Game does not exist");
    }

    private GameModel GetRequiredGameForUser(string? gameId, string userId)
    {
        gameId = NormalizeRequired(gameId, "Game id is required");

        return _games.TryGetValue(gameId, out var game) && game.GetPlayerByUserId(userId) is not null
            ? game
            : throw new HubException("Game does not exist");
    }

    private static PlayerModel GetRequiredPlayer(GameModel game, string userId)
    {
        return game.GetPlayerByUserId(userId) ?? throw new HubException("Player does not exist");
    }

    private string GetRequiredUserId(string connectionId)
    {
        return _connections.GetKeyByConnection(connectionId) ?? throw new HubException("Connection is not registered");
    }

    private bool HasWaitingGameCreatedBy(string userId)
    {
        foreach (var game in _games.Values)
        {
            if (game.IsWaitingForSecondPlayer() && game.GetPlayerByUserId(userId) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private bool CancelWaitingGamesCreatedBy(string userId)
    {
        var removed = false;

        // Removing while enumerating is safe for Dictionary since .NET Core 3.0.
        foreach (var (gameId, game) in _games)
        {
            if (game.IsWaitingForSecondPlayer() && game.GetPlayerByUserId(userId) is not null)
            {
                _games.Remove(gameId);
                removed = true;
            }
        }

        return removed;
    }

    private bool CancelWaitingGame(string gameId, string userId)
    {
        return _games.TryGetValue(gameId, out var game) &&
               game.IsWaitingForSecondPlayer() &&
               game.GetPlayerByUserId(userId) is not null &&
               _games.Remove(gameId);
    }

    private void PruneExpiredGamesIfDue()
    {
        var now = _timeProvider.GetTimestamp();
        if (now < _nextPruneTimestamp)
        {
            return;
        }

        _nextPruneTimestamp = now + _pruneIntervalTicks;

        foreach (var (gameId, game) in _games)
        {
            if (IsExpired(game, now))
            {
                _games.Remove(gameId);
            }
        }
    }

    private bool IsExpired(GameModel game, long now)
    {
        return (game.IsWaitingForSecondPlayer() && _timeProvider.GetElapsedTime(game.CreatedTimestamp, now) > WaitingGameLifetime) ||
               (game.FinishedTimestamp is { } finished && _timeProvider.GetElapsedTime(finished, now) > FinishedGameLifetime) ||
               (game.IsGameInProgress() && _timeProvider.GetElapsedTime(game.LastActivityTimestamp, now) > InactiveGameLifetime);
    }

    /// <summary>
    /// Keeps memory bounded whatever clients do: past the cap, the least recently active games that
    /// are not being played go first. A game in progress is never evicted.
    /// </summary>
    private void EvictExcessGames()
    {
        var excessGameCount = _games.Count - MaxStoredGames;
        if (excessGameCount <= 0)
        {
            return;
        }

        List<GameModel> removableGames = [with(capacity: _games.Count)];
        foreach (var game in _games.Values)
        {
            if (!game.IsGameInProgress())
            {
                removableGames.Add(game);
            }
        }

        removableGames.Sort(static (left, right) => left.LastActivityTimestamp.CompareTo(right.LastActivityTimestamp));

        var removeCount = Math.Min(excessGameCount, removableGames.Count);
        for (var i = 0; i < removeCount; i++)
        {
            _games.Remove(removableGames[i].Id);
        }
    }

    private void AddGameStatusForAllPlayers(List<ClientMessage> messages, GameModel game)
    {
        // One snapshot for both players: only their index into it differs.
        var players = game.CreatePlayerSnapshots();
        for (var i = 0; i < game.Players.Count; i++)
        {
            messages.Add(ClientMessage.To(
                _connections.GetConnectionsByKey(game.Players[i].UserId),
                ClientMethodNames.GameStatus,
                new GameStatusMessage(new GameStatusForPlayerModel(game.Id, players, i))));
        }
    }

    private static ClientMessage BuildGameStatusMessage(GameModel game, PlayerModel player, string[] connectionIds)
    {
        return ClientMessage.To(
            connectionIds,
            ClientMethodNames.GameStatus,
            new GameStatusMessage(new GameStatusForPlayerModel(game.Id, game.CreatePlayerSnapshots(), game.IndexOfPlayer(player))));
    }

    private static ClientMessage BuildHasInProgressGameMessage(string[] connectionIds, bool hasInProgressGame)
    {
        return ClientMessage.To(
            connectionIds,
            ClientMethodNames.HasInProgressGame,
            hasInProgressGame ? PlayerHasInProgressGameMessage.Yes : PlayerHasInProgressGameMessage.No);
    }

    private void AddConnectionStatusChanged(List<ClientMessage> messages, GameModel game, PlayerModel player, bool isConnected)
    {
        var message = new PlayerOnlyConnectionStatusChangedMessage(game.Id, player.Name, player.TeamId, isConnected);
        game.AddToFeed(message);
        messages.Add(ClientMessage.To(GetConnectionsOfPlayers(game), ClientMethodNames.PlayerOnlyConnectionStatusChanged, message));
    }

    private static void AddFeedReplay(List<ClientMessage> messages, GameModel game, string connectionId)
    {
        string[] recipient = [connectionId];
        foreach (var entry in game.Feed)
        {
            messages.Add(ClientMessage.To(recipient, entry.ClientMethodName, entry.Message));
        }
    }

    /// <summary>
    /// The waiting games, pushed to every connection. Each creator's connections get their own
    /// copy in which their own game carries their user id, which is how the UI recognises it;
    /// everyone else gets the list with every user id blank.
    /// </summary>
    private void AddGameListBroadcast(List<ClientMessage> messages)
    {
        var waitingGames = GetWaitingGamesNewestFirst();
        var publicList = new GameListItem[waitingGames.Count];
        for (var i = 0; i < publicList.Length; i++)
        {
            publicList[i] = BuildGameListItem(waitingGames[i], includeUserId: false);
        }

        List<string>? creatorConnectionIds = null;
        HashSet<string>? creatorsServed = null;
        foreach (var game in waitingGames)
        {
            var creatorId = game.Players[0].UserId;
            if (!(creatorsServed ??= [with(StringComparer.Ordinal)]).Add(creatorId))
            {
                continue;
            }

            var creatorConnections = _connections.GetConnectionsByKey(creatorId);
            if (creatorConnections.Length == 0)
            {
                continue;
            }

            (creatorConnectionIds ??= []).AddRange(creatorConnections);
            messages.Add(ClientMessage.To(
                creatorConnections,
                ClientMethodNames.GameList,
                new GameListMessage(BuildGameListForUser(creatorId, waitingGames, publicList))));
        }

        messages.Add(ClientMessage.ToAllExcept(
            (IReadOnlyList<string>?)creatorConnectionIds ?? [],
            ClientMethodNames.GameList,
            new GameListMessage(publicList)));
    }

    private ClientMessage BuildGameListMessageForUser(string userId, string[] connectionIds)
    {
        var waitingGames = GetWaitingGamesNewestFirst();
        var gameList = new GameListItem[waitingGames.Count];
        for (var i = 0; i < gameList.Length; i++)
        {
            var game = waitingGames[i];
            gameList[i] = BuildGameListItem(game, includeUserId: IsCreatedBy(game, userId));
        }

        return ClientMessage.To(connectionIds, ClientMethodNames.GameList, new GameListMessage(gameList));
    }

    /// <summary>The public list with the user's own games swapped for copies that carry their id.</summary>
    private static GameListItem[] BuildGameListForUser(string userId, List<GameModel> waitingGames, GameListItem[] publicList)
    {
        var gameList = (GameListItem[])publicList.Clone();
        for (var i = 0; i < gameList.Length; i++)
        {
            if (IsCreatedBy(waitingGames[i], userId))
            {
                gameList[i] = gameList[i] with { UserId = userId };
            }
        }

        return gameList;
    }

    /// <summary>The newest <see cref="MaxListedGames"/> games waiting for an opponent, newest first.</summary>
    private List<GameModel> GetWaitingGamesNewestFirst()
    {
        List<GameModel> waitingGames = [];
        foreach (var game in _games.Values)
        {
            if (game.IsWaitingForSecondPlayer())
            {
                waitingGames.Add(game);
            }
        }

        waitingGames.Sort(static (left, right) => right.CreatedTimestamp.CompareTo(left.CreatedTimestamp));

        if (waitingGames.Count > MaxListedGames)
        {
            waitingGames.RemoveRange(MaxListedGames, waitingGames.Count - MaxListedGames);
        }

        return waitingGames;
    }

    private static GameListItem BuildGameListItem(GameModel game, bool includeUserId)
    {
        var creator = game.Players[0];
        return new GameListItem(
            game.Id,
            includeUserId ? creator.UserId : string.Empty,
            creator.Name,
            creator.TeamId,
            game.IsPasswordProtected());
    }

    private static bool IsCreatedBy(GameModel game, string userId)
    {
        return string.Equals(game.Players[0].UserId, userId, StringComparison.Ordinal);
    }

    private string[] GetConnectionsOfPlayers(GameModel game)
    {
        var players = game.Players;
        if (players.Count == 1)
        {
            return _connections.GetConnectionsByKey(players[0].UserId);
        }

        return [.. _connections.GetConnectionsByKey(players[0].UserId), .. _connections.GetConnectionsByKey(players[1].UserId)];
    }

    private static string NormalizeRequired(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new HubException(message);
        }

        return value.Trim();
    }

    private static string NormalizeUserId(string? userId)
    {
        var normalized = NormalizeRequired(userId, "User id is required");
        return normalized.Length <= MaxUserIdLength ? normalized : throw new HubException("User id is invalid");
    }

    private static string NormalizeUserName(string? userName)
    {
        return Truncate(NormalizeRequired(userName, "User name is required"), MaxUserNameLength);
    }

    /// <summary>Cuts to at most <paramref name="maxLength"/> characters without splitting a surrogate pair.</summary>
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        // Half an emoji would reach every client as an invalid character.
        var length = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..length];
    }

    /// <summary>Constant-time, so the response time says nothing about how much of a guess was right.</summary>
    private static bool PasswordMatches(string expected, string? actual)
    {
        return actual is not null &&
               CryptographicOperations.FixedTimeEquals(MemoryMarshal.AsBytes(expected.AsSpan()), MemoryMarshal.AsBytes(actual.AsSpan()));
    }

    private static void ValidateTeam(TeamEnum teamId)
    {
        if (teamId is not TeamEnum.Cats and not TeamEnum.Mouse)
        {
            throw new HubException("Team is invalid");
        }
    }

    private static TeamEnum GetOppositeTeam(TeamEnum teamId)
    {
        return teamId switch
        {
            TeamEnum.Cats => TeamEnum.Mouse,
            TeamEnum.Mouse => TeamEnum.Cats,
            _ => throw new HubException("Team is invalid")
        };
    }
}
