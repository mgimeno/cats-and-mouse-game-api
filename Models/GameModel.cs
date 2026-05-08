using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Interfaces;

namespace CatsAndMouseApi.Models
{
    public class GameModel
    {
        private const int BoardSize = 8;
        private const int BoardSquareCount = BoardSize * BoardSize;

        public string Id { get; set; }
        public string? Password { get; set; }
        public string? RematchGameId { get; set; }
        public List<PlayerModel> Players { get; set; }
        public List<IMessageToClient> ChatMessages { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime LastActivityUtc { get; set; }
        public DateTime? DateStarted { get; set; } = null;
        public DateTime? DateFinished { get; set; } = null;

        public GameModel(string? gamePassword = null)
        {
            this.Id = Guid.NewGuid().ToString("N")[..5];
            this.Password = gamePassword;

            this.Players = new List<PlayerModel>(2);
            this.ChatMessages = [];

            this.DateCreated = DateTime.UtcNow;
            this.LastActivityUtc = this.DateCreated;
        }

        public void SetFirstPlayer(TeamEnum teamId, string userName, string userId)
        {
            SetPlayer(teamId, userName, userId);
        }

        public void SetSecondPlayer(string userName, string userId)
        {
            if (!IsTeamAlreadyConnected(TeamEnum.Cats))
            {
                SetPlayer(TeamEnum.Cats, userName, userId);
            }
            else
            {
                SetPlayer(TeamEnum.Mouse, userName, userId);
            }
        }

        public bool IsTeamAlreadyConnected(TeamEnum teamId)
        {
            foreach (var player in this.Players)
            {
                if (player.TeamId == teamId)
                {
                    return true;
                }
            }

            return false;
        }

        public void Start()
        {
            var mousePlayer = GetPlayerByTeam(TeamEnum.Mouse)
                ?? throw new InvalidOperationException("Mouse player does not exist");

            mousePlayer.IsTheirTurn = true;
            this.DateStarted = DateTime.UtcNow;
            Touch();

            RecalculateFiguresCanMoveToPositions();
        }

        public PlayerModel? GetPlayerByUserId(string userId)
        {
            foreach (var player in this.Players)
            {
                if (player.UserId == userId)
                {
                    return player;
                }
            }

            return null;
        }

        public FigureModel? GetPlayerFigure(PlayerModel? player, int figureId)
        {
            if (player == null)
            {
                return null;
            }

            foreach (var figure in player.Figures)
            {
                if (figure.Id == figureId)
                {
                    return figure;
                }
            }

            return null;
        }

        public List<string> GetPlayersUsersIds()
        {
            var userIds = new List<string>(this.Players.Count);
            foreach (var player in this.Players)
            {
                userIds.Add(player.UserId);
            }

            return userIds;
        }


        public void RecalculateFiguresCanMoveToPositions()
        {
            Span<bool> occupiedPositions = stackalloc bool[BoardSquareCount];
            occupiedPositions.Clear();

            foreach (var player in this.Players)
            {
                foreach (var figure in player.Figures)
                {
                    if (TryGetBoardIndex(figure.Position.RowIndex, figure.Position.ColumnIndex, out var boardIndex))
                    {
                        occupiedPositions[boardIndex] = true;
                    }
                }
            }

            foreach (var player in this.Players)
            {
                foreach (var figure in player.Figures)
                {
                    figure.CanMoveToPositions.Clear();

                    var moveUpwardsRowIndex = figure.Position.RowIndex - 1;
                    var moveDownwardsRowIndex = figure.Position.RowIndex + 1;

                    var moveLeftwards = figure.Position.ColumnIndex - 1;
                    var moveRightwards = figure.Position.ColumnIndex + 1;

                    if (figure.TypeId == FigureTypeEnum.Mouse)
                    {

                        //up-left
                        if (IsNewPositionValid(moveUpwardsRowIndex, moveLeftwards, occupiedPositions))
                        {
                            figure.AddCanMoveToPosition(moveUpwardsRowIndex, moveLeftwards);
                        }

                        //up-right
                        if (IsNewPositionValid(moveUpwardsRowIndex, moveRightwards, occupiedPositions))
                        {
                            figure.AddCanMoveToPosition(moveUpwardsRowIndex, moveRightwards);
                        }

                    }

                    //down-left
                    if (IsNewPositionValid(moveDownwardsRowIndex, moveLeftwards, occupiedPositions))
                    {
                        figure.AddCanMoveToPosition(moveDownwardsRowIndex, moveLeftwards);
                    }

                    //down-right
                    if (IsNewPositionValid(moveDownwardsRowIndex, moveRightwards, occupiedPositions))
                    {
                        figure.AddCanMoveToPosition(moveDownwardsRowIndex, moveRightwards);
                    }


                }
            }

        }

        public bool CanMove(FigureModel figure, int rowIndex, int columnIndex)
        {
            foreach (var position in figure.CanMoveToPositions)
            {
                if (position.RowIndex == rowIndex && position.ColumnIndex == columnIndex)
                {
                    return true;
                }
            }

            return false;
        }

        public void Move(FigureModel figure, int rowIndex, int columnIndex)
        {
            figure.ChangePosition(rowIndex, columnIndex);

            RecalculateFiguresCanMoveToPositions();

            CheckForGameOver();
            Touch();
        }

        public bool IsGameOver()
        {
            foreach (var player in this.Players)
            {
                if (player.IsWinner)
                {
                    return true;
                }
            }

            return false;
        }

        public PlayerModel? GetWinnerPlayer()
        {
            foreach (var player in this.Players)
            {
                if (player.IsWinner)
                {
                    return player;
                }
            }

            return null;
        }

        public void SetNextTurn()
        {
            foreach (var player in this.Players)
            {
                player.IsTheirTurn = !player.IsTheirTurn;
            }
        }

        public PlayerModel? GetCurrentTurnPlayer()
        {
            foreach (var player in this.Players)
            {
                if (player.IsTheirTurn)
                {
                    return player;
                }
            }

            return null;
        }

        public bool IsWaitingForSecondPlayer()
        {
            return this.DateStarted == null && this.Players.Count == 1;
        }

        public bool IsGameInProgress()
        {
            return (this.DateStarted.HasValue && !IsGameOver());
        }

        public bool IsPasswordProtected()
        {
            return !string.IsNullOrWhiteSpace(this.Password);
        }

        public bool HasAnyPlayerLeft()
        {
            foreach (var player in this.Players)
            {
                if (player.HasUserLeftTheGame)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsReadyForRematch()
        {
            if (!this.IsGameOver() || this.HasAnyPlayerLeft())
            {
                return false;
            }

            foreach (var player in this.Players)
            {
                if (!player.WantsToRematch)
                {
                    return false;
                }
            }

            return true;
        }

        public PlayerModel? GetOpponentPlayer(PlayerModel player)
        {
            foreach (var opponent in this.Players)
            {
                if (opponent.UserId != player.UserId)
                {
                    return opponent;
                }
            }

            return null;
        }

        public void PlayerLeft(PlayerModel playerWhoLeft)
        {

            playerWhoLeft.HasUserLeftTheGame = true;
            Touch();

            if (this.IsGameInProgress())
            {
                PlayerSurrenders(playerWhoLeft);
            }
        }

        public void PlayerSurrenders(PlayerModel playerWhoSurrenders)
        {
            playerWhoSurrenders.IsWinner = false;
            var opponentPlayer = GetPlayerByTeam(playerWhoSurrenders.TeamId == TeamEnum.Cats ? TeamEnum.Mouse : TeamEnum.Cats)
                ?? throw new InvalidOperationException("Opponent player does not exist");
            opponentPlayer.IsWinner = true;

            foreach (var player in this.Players)
            {
                player.IsTheirTurn = false;
            }

            this.DateFinished = DateTime.UtcNow;
            Touch();
        }

        public void Touch()
        {
            this.LastActivityUtc = DateTime.UtcNow;
        }

        private void CheckForGameOver()
        {
            var mousePlayer = GetPlayerByTeam(TeamEnum.Mouse) as MousePlayerModel
                ?? throw new InvalidOperationException("Mouse player does not exist");
            var catsPlayer = GetPlayerByTeam(TeamEnum.Cats) as CatsPlayerModel
                ?? throw new InvalidOperationException("Cats player does not exist");

            if (mousePlayer.Figures[0].Position.RowIndex == 0)
            {
                mousePlayer.IsWinner = true;
            }
            else
            {
                PlayerModel nextTurnPlayer = mousePlayer.IsTheirTurn ? catsPlayer : mousePlayer;

                var canNextTurnPlayerMoveAnyFigure = false;
                foreach (var figure in nextTurnPlayer.Figures)
                {
                    if (figure.CanMoveToPositions.Count > 0)
                    {
                        canNextTurnPlayerMoveAnyFigure = true;
                        break;
                    }
                }

                if (!canNextTurnPlayerMoveAnyFigure)
                {
                    var currentTurnPlayer = GetCurrentTurnPlayer()
                        ?? throw new InvalidOperationException("Current turn player does not exist");
                    currentTurnPlayer.IsWinner = true;
                }
            }

            if (IsGameOver())
            {
                foreach (var player in Players)
                {
                    player.IsTheirTurn = false;
                }

                this.DateFinished = DateTime.UtcNow;
            }
        }

        private PlayerModel? GetPlayerByTeam(TeamEnum teamId)
        {
            foreach (var player in this.Players)
            {
                if (player.TeamId == teamId)
                {
                    return player;
                }
            }

            return null;
        }

        private void SetPlayer(TeamEnum teamId, string userName, string userId)
        {
            PlayerModel player;
            if (teamId == TeamEnum.Cats)
            {
                player = new CatsPlayerModel();
            }
            else if (teamId == TeamEnum.Mouse)
            {
                player = new MousePlayerModel();
            }
            else
            {
                throw new InvalidOperationException("Team is invalid");
            }

            player.Name = userName;
            player.UserId = userId;

            this.Players.Add(player);
        }

        private static bool IsNewPositionValid(int rowIndex, int columnIndex, ReadOnlySpan<bool> occupiedPositions)
        {
            if (!TryGetBoardIndex(rowIndex, columnIndex, out var boardIndex))
            {
                return false;
            }

            return !occupiedPositions[boardIndex];

        }

        private static bool TryGetBoardIndex(int rowIndex, int columnIndex, out int boardIndex)
        {
            if ((uint)rowIndex >= BoardSize || (uint)columnIndex >= BoardSize)
            {
                boardIndex = -1;
                return false;
            }

            boardIndex = (rowIndex * BoardSize) + columnIndex;
            return true;
        }

    }
}
