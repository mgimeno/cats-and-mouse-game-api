
using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Hubs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CatsAndMouseGame.Models
{
    public class GameModel
    {
        public string Id { get; set; }
        public string Password { get; set; }
        public List<PlayerModel> Players { get; set; }
        public List<IMessageToClient> ChatMessages { get; set; } 
        public DateTime DateCreated { get; set; }
        public DateTime? DateStarted { get; set; } = null;
        public DateTime? DateFinished { get; set; } = null;

        public GameModel(string gamePassword = null)
        {
            this.Id = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 5);
            this.Password = gamePassword;

            this.Players = new List<PlayerModel>();
            this.ChatMessages = new List<IMessageToClient>();

            this.DateCreated = DateTime.UtcNow;
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
            return this.Players.Any(p => p.TeamId == teamId);
        }

        public void Start()
        {
            var mousePlayer = GetPlayerByTeam(TeamEnum.Mouse);

            mousePlayer.IsTheirTurn = true;
            this.DateStarted = DateTime.UtcNow;

            RecalculateFiguresCanMoveToPositions();
        }

        public PlayerModel GetPlayerByUserId(string userId)
        {
            return this.Players.Where(p => p.UserId == userId).FirstOrDefault();
        }

        public FigureModel GetPlayerFigure(PlayerModel player, int figureId)
        {
            if (player == null)
            {
                return null;
            }

            return player.Figures.Where(c => c.Id == figureId).FirstOrDefault();
        }

        public List<string> GetPlayersUsersIds()
        {
            return this.Players.Select(p => p.UserId).ToList();
        }


        public void RecalculateFiguresCanMoveToPositions()
        {

            foreach (var player in this.Players)
            {

                foreach (var figure in player.Figures)
                {

                    figure.CanMoveToPositions = new List<FigurePositionModel>();

                    var moveUpwardsRowIndex = figure.Position.RowIndex - 1;
                    var moveDownwardsRowIndex = figure.Position.RowIndex + 1;

                    var moveLeftwards = figure.Position.ColumnIndex - 1;
                    var moveRightwards = figure.Position.ColumnIndex + 1;

                    if (figure.TypeId == FigureTypeEnum.Mouse)
                    {

                        //up-left
                        if (this.IsNewPositionValid(moveUpwardsRowIndex, moveLeftwards))
                        {
                            figure.AddCanMoveToPosition(moveUpwardsRowIndex, moveLeftwards);
                        }

                        //up-right
                        if (this.IsNewPositionValid(moveUpwardsRowIndex, moveRightwards))
                        {
                            figure.AddCanMoveToPosition(moveUpwardsRowIndex, moveRightwards);
                        }

                    }

                    //down-left
                    if (this.IsNewPositionValid(moveDownwardsRowIndex, moveLeftwards))
                    {
                        figure.AddCanMoveToPosition(moveDownwardsRowIndex, moveLeftwards);
                    }

                    //down-right
                    if (this.IsNewPositionValid(moveDownwardsRowIndex, moveRightwards))
                    {
                        figure.AddCanMoveToPosition(moveDownwardsRowIndex, moveRightwards);
                    }


                }
            }

        }

        public bool CanMove(FigureModel figure, int rowIndex, int columnIndex)
        {
            if (figure.CanMoveToPositions.Any(p => p.RowIndex == rowIndex && p.ColumnIndex == columnIndex))
            {
                return true;
            }

            return false;
        }

        public void Move(FigureModel figure, int rowIndex, int columnIndex)
        {
            figure.ChangePosition(rowIndex, columnIndex);

            RecalculateFiguresCanMoveToPositions();

            CheckForGameOver();
        }

        public bool IsGameOver()
        {
            return this.Players.Any(p => p.IsWinner == true);
        }

        public PlayerModel GetWinnerPlayer()
        {
            return this.Players.Where(p => p.IsWinner).FirstOrDefault();
        }

        public void SetNextTurn()
        {
            this.Players.ForEach(p => p.IsTheirTurn = !p.IsTheirTurn);
        }

        public PlayerModel GetCurrentTurnPlayer()
        {
            return this.Players.Where(p => p.IsTheirTurn).FirstOrDefault();
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

        public bool HasAnyPlayerLeft() {
            return this.Players.Any(p => p.HasUserLeftTheGame);
        }

        public bool IsReadyForRematch() {
            return this.IsGameOver() && !this.HasAnyPlayerLeft() && this.Players.All(p => p.WantsToRematch);
        }

        public PlayerModel GetOpponentPlayer(PlayerModel player)
        {
            return this.Players.Where(p => p.UserId != player.UserId).FirstOrDefault();
        }

        public void PlayerLeft(PlayerModel playerWhoLeft)
        {

            playerWhoLeft.HasUserLeftTheGame = true;

            if (this.IsGameInProgress())
            {
                PlayerSurrenders(playerWhoLeft);
            }
        }

        public void PlayerSurrenders(PlayerModel playerWhoSurrenders) {
            playerWhoSurrenders.IsWinner = false;
            var opponentPlayer = GetPlayerByTeam(playerWhoSurrenders.TeamId == TeamEnum.Cats ? TeamEnum.Mouse : TeamEnum.Cats);
            opponentPlayer.IsWinner = true;

            this.Players.ForEach(p => p.IsTheirTurn = false);
            this.DateFinished = DateTime.UtcNow;
        }

        private void CheckForGameOver()
        {
            var mousePlayer = GetPlayerByTeam(TeamEnum.Mouse) as MousePlayerModel;
            var catsPlayer = GetPlayerByTeam(TeamEnum.Cats) as CatsPlayerModel;

            if (mousePlayer.Figures[0].Position.RowIndex == 0)
            {
                mousePlayer.IsWinner = true;
            }
            else
            {
                var nextTurnPlayer = (mousePlayer.IsTheirTurn ? catsPlayer as PlayerModel : mousePlayer as PlayerModel);

                var canNextTurnPlayerMoveAnyFigure = nextTurnPlayer.Figures.Any(f => f.CanMoveToPositions.Any());

                if (!canNextTurnPlayerMoveAnyFigure)
                {
                    var currentTurnPlayer = GetCurrentTurnPlayer();
                    currentTurnPlayer.IsWinner = true;
                }
            }

            if (IsGameOver())
            {
                Players.ForEach(p => p.IsTheirTurn = false);
                this.DateFinished = DateTime.UtcNow;
            }
        }

        private PlayerModel GetPlayerByTeam(TeamEnum teamId)
        {
            return this.Players.Where(p => p.TeamId == teamId).FirstOrDefault();
        }

        private void SetPlayer(TeamEnum teamId, string userName, string userId)
        {
            PlayerModel player;
            if (teamId == TeamEnum.Cats)
            {
                player = new CatsPlayerModel();
            }
            else
            {
                player = new MousePlayerModel();
            }

            player.Name = userName;
            player.UserId = userId;

            this.Players.Add(player);
        }

        private bool IsNewPositionValid(int rowIndex, int columnIndex)
        {

            if (rowIndex < 0 || rowIndex > 7 || columnIndex < 0 || columnIndex > 7)
            {
                //Position is out of the chess board
                return false;
            }

            if (this.IsPositionCurrentlyTaken(rowIndex, columnIndex))
            {
                return false;
            }

            return true;

        }

        private bool IsPositionCurrentlyTaken(int rowIndex, int columnIndex)
        {
            return this.Players.Any(p => p.Figures.Any(f => f.Position.RowIndex == rowIndex && f.Position.ColumnIndex == columnIndex));
        }



    }
}
