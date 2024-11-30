using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CatsAndMouseGame.Hubs
{
    [EnableCors("CorsPolicy")]
    public class GameHub : Hub
    {

        private static readonly List<GameModel> _games = new List<GameModel>();

        private static readonly ConnectionMapping<string> _connections = new ConnectionMapping<string>();

        public async override Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public async Task RegisterConnection(string userId)
        {
            if (!_connections.GetConnectionsByKey(userId).Contains(Context.ConnectionId))
            {
                _connections.Add(userId, Context.ConnectionId);
            }

            var playerInProgressGame = GetInProgressGame();
            if (playerInProgressGame != null)
            {
                await SendInProgressGameStatusToCaller();
                await Task.Delay(3000); // Delay to make sure the UI for the chat is ready to receive messages
                await SendChatHistoryToCaller(playerInProgressGame);
                if (_connections.GetConnectionsByKey(userId).Count == 1)
                {
                    await SendPlayerConnectionStatusChangedMessageToAllAsync(playerInProgressGame, userId, isConnected: true);
                }
            }
            else
            {
                await SendGamesAwaitingForSecondPlayerToCallerAsync();
            }

            await SendWhetherHasInProgressGameToCaller();
        }

        public async override Task OnDisconnectedAsync(Exception exception)
        {
            var playerInProgressGame = GetInProgressGame();
            var userId = GetUserIdByCurrentConnectionId();
            var result = _connections.RemoveConnection(Context.ConnectionId);
            
            if (!result.HasOtherActiveConnections)
            {
                await CancelGamesThatHaveNotStartedCreatedByUser(result.Key);
                if (playerInProgressGame != null)
                {
                    await SendPlayerConnectionStatusChangedMessageToAllAsync(playerInProgressGame, userId, isConnected: false);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task<GameListItem> CreateGame(CreateGameModel model)
        {
            var userId = GetUserIdByCurrentConnectionId();

            if (GetGamesAwaitingForSecondPlayer().Any(g => g.UserId == userId))
            {
                throw new Exception("You are already creating another game");
            }

            var newGame = new GameModel(model.GamePassword);
            newGame.SetFirstPlayer(model.TeamId, model.UserName, userId);

            _games.Add(newGame);

            await SendGamesAwaitingForSecondPlayerToAllClientsAsync();

            return BuildGameListItem(newGame);
        }

        public async Task JoinGame(JoinGameModel model)
        {
            var game = _games.Where(g => g.Id == model.GameId).FirstOrDefault();

            if (game == null)
            {
                throw new Exception("Game does not exist");
            }

            if (game.IsPasswordProtected() && (game.Password != model.GamePassword))
            {
                throw new Exception("Game password is invalid");
            }

            if (!game.IsWaitingForSecondPlayer())
            {
                throw new Exception("Game is in progress or over");
            }

            if (game.Players[0].UserId == GetUserIdByCurrentConnectionId())
            {
                throw new Exception("You cannot join your own game");
            }

            game.SetSecondPlayer(model.UserName, GetUserIdByCurrentConnectionId());

            game.Start();

            var allPlayersConnections = GetAllConnectionsByUsersIds(game.GetPlayersUsersIds());

            await SendMessageToClientsAsync("GameStart", allPlayersConnections, new GameStartMessage());

            await SendGamesAwaitingForSecondPlayerToAllClientsAsync();
        }

        public async Task Move(MoveFigureModel model)
        {
            var game = GetInProgressGame();

            if (game == null)
            {
                throw new Exception("Game does not exist");
            }

            if (game.IsGameOver())
            {
                throw new Exception("Game is over");
            }

            var player = game.GetPlayerByUserId(GetUserIdByCurrentConnectionId());

            if (player == null)
            {
                throw new Exception("Player does not exist");
            }

            if (!player.IsTheirTurn)
            {
                throw new Exception("It's not your turn");
            }

            var figure = game.GetPlayerFigure(player, model.FigureId);

            if (figure == null)
            {
                throw new Exception("Figure does not exist");
            }

            if (!game.CanMove(figure, model.RowIndex, model.ColumnIndex))
            {
                throw new Exception("This figure cannot be moved to that position");
            }

            game.Move(figure, model.RowIndex, model.ColumnIndex);

            if (game.IsGameOver())
            {
                await SendGameStatusToAllPlayers(game);
            }
            else
            {
                game.SetNextTurn();

                await SendGameStatusToAllPlayers(game);
            }
        }

        public async Task CancelGameThatHasNotStarted(CancelGameModel model)
        {
            var game = _games
                .Where(g => g.Id == model.GameId)
                .Where(g => g.IsWaitingForSecondPlayer())
                .Where(g => g.GetPlayerByUserId(model.UserId) != null)
                .FirstOrDefault();

            if (game != null)
            {
                _games.Remove(game);


                if (model.SendAwaitingGamesToAllClients)
                {
                    await SendGamesAwaitingForSecondPlayerToAllClientsAsync();
                }
            }
        }

        public async Task SendInProgressGameStatusToCaller()
        {
            var game = GetInProgressGame();

            if (game == null)
            {
                throw new Exception("Game does not exist");
            }

            var player = game.GetPlayerByUserId(GetUserIdByCurrentConnectionId());

            await SendGameStatusToPlayer(game, player);
        }

        public async Task SendWhetherHasInProgressGameToCaller()
        {
            var game = GetInProgressGame();

            var message = new PlayerHasInProgressGameMessage
            {
                HasInProgressGame = (game != null)
            };

            await SendMessageToClientsAsync("HasInProgressGame", GetAllConnectionsOfCurrentConnectionUser(), message);
        }

        public async Task SendChatMessage(ChatLineSentByClientModel model)
        {

            var game = GetGame(model.GameId);

            var player = game.GetPlayerByUserId(GetUserIdByCurrentConnectionId());

            var message = new ChatMessage
            {
                GameId = model.GameId,
                ChatLine = new ChatLineModel
                {
                    UserName = player.Name,
                    TeamId = player.TeamId,
                    Message = model.Message
                }
            };

            game.ChatMessages.Add(message);

            var allPlayersConnections = GetAllConnectionsByUsersIds(game.GetPlayersUsersIds());

            await SendMessageToClientsAsync("ChatMessage", allPlayersConnections, message);

        }

        public async Task ExitGame(GameIdModel model)
        {
            var game = GetGame(model.GameId);

            var playerWhoLeft = game.GetPlayerByUserId(GetUserIdByCurrentConnectionId());
            var opponentPlayer = game.GetOpponentPlayer(playerWhoLeft);

            var message = new PlayerHasLeftGameMessage
            {
                GameId = game.Id,
                UserName = playerWhoLeft.Name,
                TeamId = playerWhoLeft.TeamId
            };

            game.ChatMessages.Add(message);

            await SendMessageToClientsAsync("PlayerHasLeftGame", GetAllConnectionsByUsersIds(new List<string>() { opponentPlayer.UserId }), message);

            game.PlayerLeft(playerWhoLeft);

            if (!opponentPlayer.HasUserLeftTheGame)
            {
                await SendGameStatusToPlayer(game, opponentPlayer);
            }
        }

        public async Task Surrender()
        {
            var game = GetInProgressGame();
            if (game == null)
            {
                throw new Exception("Game does not exist");
            }

            var playerWhoSurrenders = game.GetPlayerByUserId(GetUserIdByCurrentConnectionId());

            var message = new PlayerHasSurrenderedMessage
            {
                GameId = game.Id,
                UserName = playerWhoSurrenders.Name,
                TeamId = playerWhoSurrenders.TeamId
            };

            game.ChatMessages.Add(message);

            var allPlayersConnections = GetAllConnectionsByUsersIds(game.GetPlayersUsersIds());

            await SendMessageToClientsAsync("PlayerHasSurrendered", allPlayersConnections, message);

            game.PlayerSurrenders(playerWhoSurrenders);

            await SendGameStatusToAllPlayers(game);
        }

        public async Task PlayerWantsToRematch(GameIdModel model)
        {
            var game = GetGame(model.GameId);
            if (!game.IsGameOver())
            {
                throw new Exception("Game is not over");
            }
            if (game.HasAnyPlayerLeft())
            {
                throw new Exception("Opponent has left");
            }

            var playerWhoWantsToRematch = game.GetPlayerByUserId(GetUserIdByCurrentConnectionId());
            var opponentPlayer = game.GetOpponentPlayer(playerWhoWantsToRematch);
            var allPlayersConnections = GetAllConnectionsByUsersIds(game.GetPlayersUsersIds());

            playerWhoWantsToRematch.WantsToRematch = true;

            var message = new PlayerWantsRematchMessage
            {
                GameId = game.Id,
                UserName = playerWhoWantsToRematch.Name,
                TeamId = playerWhoWantsToRematch.TeamId
            };

            game.ChatMessages.Add(message);

            await SendMessageToClientsAsync("PlayerWantsRematch", allPlayersConnections, message);

            if (game.IsReadyForRematch())
            {

                var rematchGame = new GameModel();
                rematchGame.SetFirstPlayer(playerWhoWantsToRematch.TeamId, playerWhoWantsToRematch.Name, playerWhoWantsToRematch.UserId);
                rematchGame.SetSecondPlayer(opponentPlayer.Name, opponentPlayer.UserId);

                rematchGame.Start();

                _games.Add(rematchGame);

                await SendGameStatusToAllPlayers(rematchGame);

            }
        }


        private GameModel GetInProgressGame()
        {
            var userId = GetUserIdByCurrentConnectionId();

            var game = _games
                .Where(g => g.IsGameInProgress())
                .Where(g => g.Players.Any(p => p.UserId == userId))
                .FirstOrDefault();

            return game;
        }

        private GameModel GetGame(string gameId = null)
        {
            var userId = GetUserIdByCurrentConnectionId();

            var game = _games
                .Where(g => g.Players.Any(p => p.UserId == userId))
                .Where(g => (gameId == null || (g.Id.Equals(gameId))))
                .FirstOrDefault();

            if (game == null)
            {
                throw new Exception("Game does not exist");
            }

            return game;
        }


        private async Task SendGameStatusToAllPlayers(GameModel game)
        {
            foreach (var player in game.Players)
            {
                await SendGameStatusToPlayer(game, player);
            }

        }

        private async Task SendGameStatusToPlayer(GameModel game, PlayerModel player)
        {
            var gameStatus = GetGameStatusForPlayer(game, player);
            var userConnections = _connections.GetConnectionsByKey(player.UserId);

            var message = new GameStatusMessage
            {
                GameStatus = gameStatus
            };

            await SendMessageToClientsAsync("GameStatus", userConnections, message);
        }

        private async Task SendMessageToClientsAsync(string methodName, List<string> connectionsIds, IMessageToClient message)
        {
            await Clients.Clients(connectionsIds).SendAsync(methodName, message);
        }

        public async Task SendGamesAwaitingForSecondPlayerToCallerAsync()
        {
            var message = new GameListMessage
            {
                GameList = GetGamesAwaitingForSecondPlayer()
            };

            await SendMessageToClientsAsync("GameList", GetAllConnectionsOfCurrentConnectionUser(), message);
        }


        private async Task SendGamesAwaitingForSecondPlayerToAllClientsAsync()
        {

            var message = new GameListMessage
            {
                GameList = GetGamesAwaitingForSecondPlayer()
            };

            await SendMessageToClientsAsync("GameList", _connections.GetAllConnections(), message);
        }

        private List<GameListItem> GetGamesAwaitingForSecondPlayer()
        {
            var gamesAwaitingForSecondPlayer = new List<GameListItem>();

            _games
                .Where(g => g.IsWaitingForSecondPlayer())
                .OrderByDescending(g => g.DateCreated)
                .ToList()
                .ForEach(g => gamesAwaitingForSecondPlayer.Add(BuildGameListItem(g)));

            return gamesAwaitingForSecondPlayer;

        }

        private GameListItem BuildGameListItem(GameModel game)
        {
            return new GameListItem
            {
                GameId = game.Id,
                UserId = game.Players[0].UserId,
                UserName = game.Players[0].Name,
                TeamId = game.Players[0].TeamId,
                IsPasswordProtected = game.IsPasswordProtected()
            };
        }

        private GameStatusForPlayerModel GetGameStatusForPlayer(GameModel game, PlayerModel player)
        {
            return new GameStatusForPlayerModel
            {
                GameId = game.Id,
                Players = game.Players,
                MyPlayerIndex = game.Players.IndexOf(player)
            };

        }

        private async Task CancelGamesThatHaveNotStartedCreatedByUser(string userId)
        {
            var gamesIds = GetGamesAwaitingForSecondPlayer().Select(g => g.GameId).ToList();

            var games = _games
                .Where(g => gamesIds.Contains(g.Id))
                .Where(g => g.GetPlayerByUserId(userId) != null)
                .ToList();

            foreach (var game in games)
            {
                await CancelGameThatHasNotStarted(new CancelGameModel
                {
                    GameId = game.Id,
                    UserId = userId,
                    SendAwaitingGamesToAllClients = false
                });
            }

            await SendGamesAwaitingForSecondPlayerToAllClientsAsync();
        }

        private string GetUserIdByCurrentConnectionId()
        {
            return _connections.GetKeyByConnection(Context.ConnectionId);
        }

        private List<string> GetAllConnectionsOfCurrentConnectionUser()
        {
            var userId = GetUserIdByCurrentConnectionId();

            return _connections.GetConnectionsByKey(userId);
        }

        private List<string> GetAllConnectionsByUsersIds(List<string> usersIds)
        {
            var result = new List<string>();

            foreach (var userId in usersIds)
            {
                result.AddRange(_connections.GetConnectionsByKey(userId));
            }

            return result;
        }

        private async Task SendPlayerConnectionStatusChangedMessageToAllAsync(GameModel game, string userId , bool isConnected) {

            var player = game.GetPlayerByUserId(userId);

            var message = new PlayerOnlyConnectionStatusChangedMessage
            {
                GameId = game.Id,
                UserName = player.Name,
                TeamId = player.TeamId,
                IsConnected = isConnected
            };
            game.ChatMessages.Add(message);
            var allPlayersConnections = GetAllConnectionsByUsersIds(game.GetPlayersUsersIds());
            await SendMessageToClientsAsync("PlayerOnlyConnectionStatusChanged", allPlayersConnections, message);
        }

        private async Task SendChatHistoryToCaller(GameModel game) {

            var playerConnectionAsList = new List<string> { Context.ConnectionId };

            var messagesForChat = game.ChatMessages.Where(chatMessage => chatMessage.IsMessageForChat).ToList();

            foreach (var message in messagesForChat) {

                string messageType = string.Empty;

                switch (message.TypeId) {
                    case MessageToClientTypeEnum.ChatMessage:
                        messageType = "ChatMessage";
                        break;
                    case MessageToClientTypeEnum.PlayerHasLeftGame:
                        messageType = "PlayerHasLeftGame";
                        break;
                    case MessageToClientTypeEnum.PlayerHasSurrendered:
                        messageType = "PlayerHasSurrendered";
                        break;
                    case MessageToClientTypeEnum.PlayerOnlyConnectionStatusChanged:
                        messageType = "PlayerOnlyConnectionStatusChanged";
                        break;
                    case MessageToClientTypeEnum.PlayerWantsToRematch:
                        messageType = "PlayerWantsRematch";
                        break;
                }

                await SendMessageToClientsAsync(messageType, playerConnectionAsList, message);
            }

        }

    }
}
