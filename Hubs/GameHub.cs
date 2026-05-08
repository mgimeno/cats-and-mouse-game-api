using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;

namespace CatsAndMouseGame.Hubs
{
    [EnableCors("CorsPolicy")]
    public class GameHub : Hub
    {
        private const int MaxChatMessagesPerGame = 250;
        private const int MaxChatMessageLength = 1000;
        private const int MaxStoredGames = 500;
        private static readonly TimeSpan WaitingGameLifetime = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan FinishedGameLifetime = TimeSpan.FromHours(2);
        private static readonly TimeSpan InactiveGameLifetime = TimeSpan.FromHours(12);

        private static readonly List<GameModel> _games = new();
        private static readonly object _gamesLock = new();
        private static readonly ConnectionMapping<string> _connections = new();

        public Task RegisterConnection(string userId)
        {
            userId = NormalizeRequired(userId, "User id is required");
            var connectionCount = _connections.Add(userId, Context.ConnectionId);

            var outgoingMessages = new List<ClientMessage>();

            lock (_gamesLock)
            {
                PruneExpiredGames();
                var playerInProgressGame = GetInProgressGameForUser(userId);
                if (playerInProgressGame != null)
                {
                    var player = GetRequiredPlayer(playerInProgressGame, userId);
                    outgoingMessages.Add(BuildGameStatusMessage(playerInProgressGame, player, _connections.GetConnectionsByKey(userId)));
                    outgoingMessages.AddRange(BuildChatHistoryMessages(playerInProgressGame, Context.ConnectionId));

                    if (connectionCount == 1)
                    {
                        var connectionStatusMessage = BuildPlayerConnectionStatusChangedMessage(playerInProgressGame, userId, isConnected: true);
                        if (connectionStatusMessage != null)
                        {
                            outgoingMessages.Add(connectionStatusMessage);
                        }
                    }
                }
                else
                {
                    outgoingMessages.Add(BuildGameListMessage(_connections.GetConnectionsByKey(userId)));
                }

                outgoingMessages.Add(BuildHasInProgressGameMessage(userId, playerInProgressGame != null));
            }

            return SendMessagesAsync(outgoingMessages);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var result = _connections.RemoveConnection(Context.ConnectionId);
            if (result == null)
            {
                await base.OnDisconnectedAsync(exception);
                return;
            }

            if (!result.HasOtherActiveConnections)
            {
                var outgoingMessages = new List<ClientMessage>();

                lock (_gamesLock)
                {
                    PruneExpiredGames();
                    var playerInProgressGame = GetInProgressGameForUser(result.Key);
                    if (CancelWaitingGamesCreatedByUser(result.Key))
                    {
                        outgoingMessages.Add(BuildGameListMessage(_connections.GetAllConnections()));
                    }

                    if (playerInProgressGame != null)
                    {
                        var connectionStatusMessage = BuildPlayerConnectionStatusChangedMessage(playerInProgressGame, result.Key, isConnected: false);
                        if (connectionStatusMessage != null)
                        {
                            outgoingMessages.Add(connectionStatusMessage);
                        }
                    }
                }

                await SendMessagesAsync(outgoingMessages);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task<GameListItem> CreateGame(CreateGameModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var userId = GetRequiredUserIdByCurrentConnectionId();
            var userName = NormalizeRequired(model.UserName, "User name is required");
            ValidateTeam(model.TeamId);

            GameListItem createdGame;
            ClientMessage gameListMessage;

            lock (_gamesLock)
            {
                PruneExpiredGames();
                if (_games.Any(g => g.IsWaitingForSecondPlayer() && g.GetPlayerByUserId(userId) != null))
                {
                    throw new HubException("You are already creating another game");
                }

                var newGame = CreateGameWithUniqueId(model.GamePassword);
                newGame.SetFirstPlayer(model.TeamId, userName, userId);
                _games.Add(newGame);

                createdGame = BuildGameListItem(newGame);
                gameListMessage = BuildGameListMessage(_connections.GetAllConnections());
            }

            await SendMessageAsync(gameListMessage);
            return createdGame;
        }

        public Task JoinGame(JoinGameModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var userId = GetRequiredUserIdByCurrentConnectionId();
            var userName = NormalizeRequired(model.UserName, "User name is required");
            var gameId = NormalizeRequired(model.GameId, "Game id is required");

            var outgoingMessages = new List<ClientMessage>();

            lock (_gamesLock)
            {
                PruneExpiredGames();
                var game = _games.FirstOrDefault(g => string.Equals(g.Id, gameId, StringComparison.Ordinal));
                if (game == null)
                {
                    throw new HubException("Game does not exist");
                }

                if (game.IsPasswordProtected() && !string.Equals(game.Password, model.GamePassword, StringComparison.Ordinal))
                {
                    throw new HubException("Game password is invalid");
                }

                if (!game.IsWaitingForSecondPlayer())
                {
                    throw new HubException("Game is in progress or over");
                }

                if (game.Players[0].UserId == userId)
                {
                    throw new HubException("You cannot join your own game");
                }

                game.SetSecondPlayer(userName, userId);
                game.Start();

                outgoingMessages.Add(new ClientMessage("GameStart", GetAllConnectionsByUsersIds(game.GetPlayersUsersIds()), new GameStartMessage()));
                outgoingMessages.AddRange(BuildGameStatusMessagesForAllPlayers(game));
                outgoingMessages.Add(BuildGameListMessage(_connections.GetAllConnections()));
            }

            return SendMessagesAsync(outgoingMessages);
        }

        public Task Move(MoveFigureModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var userId = GetRequiredUserIdByCurrentConnectionId();
            var outgoingMessages = new List<ClientMessage>();

            lock (_gamesLock)
            {
                PruneExpiredGames();
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

                var figure = game.GetPlayerFigure(player, model.FigureId);
                if (figure == null)
                {
                    throw new HubException("Figure does not exist");
                }

                if (!game.CanMove(figure, model.RowIndex, model.ColumnIndex))
                {
                    throw new HubException("This figure cannot be moved to that position");
                }

                game.Move(figure, model.RowIndex, model.ColumnIndex);
                if (!game.IsGameOver())
                {
                    game.SetNextTurn();
                }

                outgoingMessages.AddRange(BuildGameStatusMessagesForAllPlayers(game));
            }

            return SendMessagesAsync(outgoingMessages);
        }

        public async Task CancelGameThatHasNotStarted(CancelGameModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var currentUserId = GetRequiredUserIdByCurrentConnectionId();
            var modelUserId = NormalizeRequired(model.UserId, "User id is required");

            if (!string.Equals(currentUserId, modelUserId, StringComparison.Ordinal))
            {
                throw new HubException("You cannot cancel another player's game");
            }

            ClientMessage? gameListMessage = null;

            lock (_gamesLock)
            {
                PruneExpiredGames();
                var removed = CancelWaitingGame(model.GameId, currentUserId);
                if (removed && model.SendAwaitingGamesToAllClients)
                {
                    gameListMessage = BuildGameListMessage(_connections.GetAllConnections());
                }
            }

            if (gameListMessage != null)
            {
                await SendMessageAsync(gameListMessage);
            }
        }

        public Task SendInProgressGameStatusToCaller()
        {
            var userId = GetRequiredUserIdByCurrentConnectionId();
            ClientMessage outgoingMessage;

            lock (_gamesLock)
            {
                PruneExpiredGames();
                var game = GetInProgressGameForUser(userId);
                if (game == null)
                {
                    outgoingMessage = BuildHasInProgressGameMessage(userId, hasInProgressGame: false);
                }
                else
                {
                    var player = GetRequiredPlayer(game, userId);
                    outgoingMessage = BuildGameStatusMessage(game, player, _connections.GetConnectionsByKey(userId));
                }
            }

            return SendMessageAsync(outgoingMessage);
        }

        public Task SendWhetherHasInProgressGameToCaller()
        {
            var userId = GetRequiredUserIdByCurrentConnectionId();
            ClientMessage outgoingMessage;

            lock (_gamesLock)
            {
                PruneExpiredGames();
                outgoingMessage = BuildHasInProgressGameMessage(userId, GetInProgressGameForUser(userId) != null);
            }

            return SendMessageAsync(outgoingMessage);
        }

        public Task SendChatMessage(ChatLineSentByClientModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var userId = GetRequiredUserIdByCurrentConnectionId();
            var messageText = NormalizeRequired(model.Message, "Message is required");
            if (messageText.Length > MaxChatMessageLength)
            {
                messageText = messageText[..MaxChatMessageLength];
            }

            ClientMessage outgoingMessage;

            lock (_gamesLock)
            {
                PruneExpiredGames();
                var game = GetRequiredGameForUser(model.GameId, userId);
                var player = GetRequiredPlayer(game, userId);

                var message = new ChatMessage
                {
                    GameId = game.Id,
                    ChatLine = new ChatLineModel
                    {
                        UserName = player.Name,
                        TeamId = player.TeamId,
                        Message = messageText
                    }
                };

                AddChatMessage(game, message);
                outgoingMessage = new ClientMessage("ChatMessage", GetAllConnectionsByUsersIds(game.GetPlayersUsersIds()), message);
            }

            return SendMessageAsync(outgoingMessage);
        }

        public Task ExitGame(GameIdModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var userId = GetRequiredUserIdByCurrentConnectionId();
            var outgoingMessages = new List<ClientMessage>();

            lock (_gamesLock)
            {
                PruneExpiredGames();
                var game = GetRequiredGameForUser(model.GameId, userId);
                var playerWhoLeft = GetRequiredPlayer(game, userId);
                var opponentPlayer = game.GetOpponentPlayer(playerWhoLeft);
                if (opponentPlayer == null)
                {
                    throw new HubException("Opponent does not exist");
                }

                var message = new PlayerHasLeftGameMessage
                {
                    GameId = game.Id,
                    UserName = playerWhoLeft.Name,
                    TeamId = playerWhoLeft.TeamId
                };

                AddChatMessage(game, message);
                outgoingMessages.Add(new ClientMessage("PlayerHasLeftGame", _connections.GetConnectionsByKey(opponentPlayer.UserId), message));

                game.PlayerLeft(playerWhoLeft);

                if (!opponentPlayer.HasUserLeftTheGame)
                {
                    outgoingMessages.Add(BuildGameStatusMessage(game, opponentPlayer, _connections.GetConnectionsByKey(opponentPlayer.UserId)));
                }
            }

            return SendMessagesAsync(outgoingMessages);
        }

        public Task Surrender()
        {
            var userId = GetRequiredUserIdByCurrentConnectionId();
            var outgoingMessages = new List<ClientMessage>();

            lock (_gamesLock)
            {
                PruneExpiredGames();
                var game = GetRequiredInProgressGame(userId);
                var playerWhoSurrenders = GetRequiredPlayer(game, userId);

                var message = new PlayerHasSurrenderedMessage
                {
                    GameId = game.Id,
                    UserName = playerWhoSurrenders.Name,
                    TeamId = playerWhoSurrenders.TeamId
                };

                AddChatMessage(game, message);
                outgoingMessages.Add(new ClientMessage("PlayerHasSurrendered", GetAllConnectionsByUsersIds(game.GetPlayersUsersIds()), message));

                game.PlayerSurrenders(playerWhoSurrenders);
                outgoingMessages.AddRange(BuildGameStatusMessagesForAllPlayers(game));
            }

            return SendMessagesAsync(outgoingMessages);
        }

        public Task PlayerWantsToRematch(GameIdModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var userId = GetRequiredUserIdByCurrentConnectionId();
            var outgoingMessages = new List<ClientMessage>();

            lock (_gamesLock)
            {
                PruneExpiredGames();
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
                var opponentPlayer = game.GetOpponentPlayer(playerWhoWantsToRematch);
                if (opponentPlayer == null)
                {
                    throw new HubException("Opponent does not exist");
                }

                if (!playerWhoWantsToRematch.WantsToRematch)
                {
                    playerWhoWantsToRematch.WantsToRematch = true;
                    game.Touch();

                    var message = new PlayerWantsRematchMessage
                    {
                        GameId = game.Id,
                        UserName = playerWhoWantsToRematch.Name,
                        TeamId = playerWhoWantsToRematch.TeamId
                    };

                    AddChatMessage(game, message);
                    outgoingMessages.Add(new ClientMessage("PlayerWantsRematch", GetAllConnectionsByUsersIds(game.GetPlayersUsersIds()), message));
                }

                if (game.IsReadyForRematch() && game.RematchGameId == null)
                {
                    var rematchGame = CreateGameWithUniqueId();
                    rematchGame.SetFirstPlayer(GetOppositeTeam(playerWhoWantsToRematch.TeamId), playerWhoWantsToRematch.Name, playerWhoWantsToRematch.UserId);
                    rematchGame.SetSecondPlayer(opponentPlayer.Name, opponentPlayer.UserId);
                    rematchGame.Start();

                    _games.Add(rematchGame);
                    game.RematchGameId = rematchGame.Id;

                    outgoingMessages.AddRange(BuildGameStatusMessagesForAllPlayers(rematchGame));
                }
            }

            return SendMessagesAsync(outgoingMessages);
        }

        public Task SendGamesAwaitingForSecondPlayerToCallerAsync()
        {
            var userId = GetRequiredUserIdByCurrentConnectionId();
            ClientMessage outgoingMessage;

            lock (_gamesLock)
            {
                PruneExpiredGames();
                outgoingMessage = BuildGameListMessage(_connections.GetConnectionsByKey(userId));
            }

            return SendMessageAsync(outgoingMessage);
        }

        private static GameModel CreateGameWithUniqueId(string? gamePassword = null)
        {
            GameModel game;
            do
            {
                game = new GameModel(gamePassword);
            }
            while (_games.Any(g => string.Equals(g.Id, game.Id, StringComparison.Ordinal)));

            return game;
        }

        private static GameModel? GetInProgressGameForUser(string userId)
        {
            return _games.FirstOrDefault(g => g.IsGameInProgress() && g.Players.Any(p => p.UserId == userId));
        }

        private static GameModel GetRequiredInProgressGame(string userId)
        {
            return GetInProgressGameForUser(userId) ?? throw new HubException("Game does not exist");
        }

        private static GameModel GetRequiredGameForUser(string gameId, string userId)
        {
            gameId = NormalizeRequired(gameId, "Game id is required");

            return _games.FirstOrDefault(g =>
                    string.Equals(g.Id, gameId, StringComparison.Ordinal) &&
                    g.Players.Any(p => p.UserId == userId))
                ?? throw new HubException("Game does not exist");
        }

        private static PlayerModel GetRequiredPlayer(GameModel game, string userId)
        {
            return game.GetPlayerByUserId(userId) ?? throw new HubException("Player does not exist");
        }

        private static bool CancelWaitingGamesCreatedByUser(string userId)
        {
            var gamesToRemove = _games
                .Where(g => g.IsWaitingForSecondPlayer() && g.GetPlayerByUserId(userId) != null)
                .ToList();

            foreach (var game in gamesToRemove)
            {
                _games.Remove(game);
            }

            return gamesToRemove.Count > 0;
        }

        private static bool CancelWaitingGame(string gameId, string userId)
        {
            gameId = NormalizeRequired(gameId, "Game id is required");

            var game = _games.FirstOrDefault(g =>
                string.Equals(g.Id, gameId, StringComparison.Ordinal) &&
                g.IsWaitingForSecondPlayer() &&
                g.GetPlayerByUserId(userId) != null);

            if (game == null)
            {
                return false;
            }

            _games.Remove(game);
            return true;
        }

        private static void AddChatMessage(GameModel game, IMessageToClient message)
        {
            game.ChatMessages.Add(message);
            game.Touch();

            if (game.ChatMessages.Count > MaxChatMessagesPerGame)
            {
                game.ChatMessages.RemoveRange(0, game.ChatMessages.Count - MaxChatMessagesPerGame);
            }
        }

        private static void PruneExpiredGames()
        {
            var now = DateTime.UtcNow;

            _games.RemoveAll(game =>
                (game.IsWaitingForSecondPlayer() && now - game.DateCreated > WaitingGameLifetime) ||
                (game.DateFinished.HasValue && now - game.DateFinished.Value > FinishedGameLifetime) ||
                (game.IsGameInProgress() && now - game.LastActivityUtc > InactiveGameLifetime));

            var excessGameCount = _games.Count - MaxStoredGames;
            if (excessGameCount <= 0)
            {
                return;
            }

            var removableGames = _games
                .Where(game => !game.IsGameInProgress())
                .OrderBy(game => game.LastActivityUtc)
                .Take(excessGameCount)
                .ToList();

            foreach (var game in removableGames)
            {
                _games.Remove(game);
            }
        }

        private static ClientMessage BuildHasInProgressGameMessage(string userId, bool hasInProgressGame)
        {
            return new ClientMessage(
                "HasInProgressGame",
                _connections.GetConnectionsByKey(userId),
                new PlayerHasInProgressGameMessage { HasInProgressGame = hasInProgressGame });
        }

        private static ClientMessage BuildGameListMessage(List<string> connectionIds)
        {
            return new ClientMessage(
                "GameList",
                connectionIds,
                new GameListMessage { GameList = BuildGamesAwaitingForSecondPlayer() });
        }

        private static List<GameListItem> BuildGamesAwaitingForSecondPlayer()
        {
            return _games
                .Where(g => g.IsWaitingForSecondPlayer())
                .OrderByDescending(g => g.DateCreated)
                .Select(BuildGameListItem)
                .ToList();
        }

        private static GameListItem BuildGameListItem(GameModel game)
        {
            var player = game.Players[0];
            return new GameListItem
            {
                GameId = game.Id,
                UserId = player.UserId,
                UserName = player.Name,
                TeamId = player.TeamId,
                IsPasswordProtected = game.IsPasswordProtected()
            };
        }

        private static List<ClientMessage> BuildGameStatusMessagesForAllPlayers(GameModel game)
        {
            return game.Players
                .Select(player => BuildGameStatusMessage(game, player, _connections.GetConnectionsByKey(player.UserId)))
                .ToList();
        }

        private static ClientMessage BuildGameStatusMessage(GameModel game, PlayerModel player, List<string> connectionIds)
        {
            var players = game.Players.Select(ClonePlayer).ToList();
            var myPlayerIndex = game.Players.IndexOf(player);

            return new ClientMessage(
                "GameStatus",
                connectionIds,
                new GameStatusMessage
                {
                    GameStatus = new GameStatusForPlayerModel
                    {
                        GameId = game.Id,
                        Players = players,
                        MyPlayerIndex = myPlayerIndex
                    }
                });
        }

        private static ClientMessage? BuildPlayerConnectionStatusChangedMessage(GameModel game, string userId, bool isConnected)
        {
            var player = game.GetPlayerByUserId(userId);
            if (player == null)
            {
                return null;
            }

            var message = new PlayerOnlyConnectionStatusChangedMessage
            {
                GameId = game.Id,
                UserName = player.Name,
                TeamId = player.TeamId,
                IsConnected = isConnected
            };

            AddChatMessage(game, message);
            return new ClientMessage("PlayerOnlyConnectionStatusChanged", GetAllConnectionsByUsersIds(game.GetPlayersUsersIds()), message);
        }

        private static List<ClientMessage> BuildChatHistoryMessages(GameModel game, string connectionId)
        {
            var connectionIds = new List<string> { connectionId };
            return game.ChatMessages
                .Where(chatMessage => chatMessage.IsMessageForChat)
                .Select(message => new ClientMessage(GetClientMethodName(message.TypeId), connectionIds, message))
                .ToList();
        }

        private static string GetClientMethodName(MessageToClientTypeEnum typeId)
        {
            return typeId switch
            {
                MessageToClientTypeEnum.ChatMessage => "ChatMessage",
                MessageToClientTypeEnum.PlayerHasLeftGame => "PlayerHasLeftGame",
                MessageToClientTypeEnum.PlayerHasSurrendered => "PlayerHasSurrendered",
                MessageToClientTypeEnum.PlayerOnlyConnectionStatusChanged => "PlayerOnlyConnectionStatusChanged",
                MessageToClientTypeEnum.PlayerWantsToRematch => "PlayerWantsRematch",
                _ => throw new HubException("Unknown chat message type")
            };
        }

        private static PlayerModel ClonePlayer(PlayerModel player)
        {
            return new PlayerModel
            {
                UserId = player.UserId,
                Name = player.Name,
                IsTheirTurn = player.IsTheirTurn,
                TeamId = player.TeamId,
                IsWinner = player.IsWinner,
                HasUserLeftTheGame = player.HasUserLeftTheGame,
                WantsToRematch = player.WantsToRematch,
                Figures = player.Figures.Select(CloneFigure).ToList()
            };
        }

        private static FigureModel CloneFigure(FigureModel figure)
        {
            return new FigureModel
            {
                Id = figure.Id,
                TypeId = figure.TypeId,
                Position = new FigurePositionModel
                {
                    RowIndex = figure.Position.RowIndex,
                    ColumnIndex = figure.Position.ColumnIndex
                },
                CanMoveToPositions = figure.CanMoveToPositions
                    .Select(position => new FigurePositionModel
                    {
                        RowIndex = position.RowIndex,
                        ColumnIndex = position.ColumnIndex
                    })
                    .ToList()
            };
        }

        private static List<string> GetAllConnectionsByUsersIds(List<string> usersIds)
        {
            return usersIds.SelectMany(userId => _connections.GetConnectionsByKey(userId)).ToList();
        }

        private string GetRequiredUserIdByCurrentConnectionId()
        {
            return _connections.GetKeyByConnection(Context.ConnectionId)
                ?? throw new HubException("Connection is not registered");
        }

        private static string NormalizeRequired(string? value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new HubException(message);
            }

            return value.Trim();
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

        private async Task SendMessagesAsync(IEnumerable<ClientMessage> messages)
        {
            foreach (var message in messages)
            {
                await SendMessageAsync(message);
            }
        }

        private Task SendMessageAsync(ClientMessage message)
        {
            return message.ConnectionIds.Count == 0
                ? Task.CompletedTask
                : Clients.Clients(message.ConnectionIds).SendAsync(message.MethodName, message.Message);
        }

        private sealed record ClientMessage(string MethodName, List<string> ConnectionIds, IMessageToClient Message);
    }
}
