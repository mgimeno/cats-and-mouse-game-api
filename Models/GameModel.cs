using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Models.MessagesToClient;

namespace CatsAndMouseApi.Models;

/// <summary>
/// One game and its rules. Not thread-safe: <see cref="Services.GameService"/> only touches a game
/// under its lock.
/// </summary>
public sealed class GameModel
{
    /// <summary>The feed keeps the latest entries only; a reconnecting client replays all of them.</summary>
    public const int MaxFeedLength = 250;

    private readonly TimeProvider _timeProvider;
    private readonly List<PlayerModel> _players = [with(capacity: 2)];
    private readonly Queue<GameFeedMessage> _feed = new();

    public GameModel(string id, string? password, TimeProvider timeProvider)
    {
        Id = id;
        Password = password;
        _timeProvider = timeProvider;
        CreatedTimestamp = timeProvider.GetTimestamp();
        LastActivityTimestamp = CreatedTimestamp;
    }

    public string Id { get; }
    public string? Password { get; }
    public string? RematchGameId { get; set; }
    public IReadOnlyList<PlayerModel> Players => _players;
    public IReadOnlyCollection<GameFeedMessage> Feed => _feed;

    // Monotonic TimeProvider timestamps rather than wall-clock times: game lifetimes are durations,
    // and a clock step (NTP, a resumed VM) must not expire or resurrect a game.
    public long CreatedTimestamp { get; }
    public long LastActivityTimestamp { get; private set; }
    public long? StartedTimestamp { get; private set; }
    public long? FinishedTimestamp { get; private set; }

    public void SetFirstPlayer(TeamEnum teamId, string userName, string userId)
    {
        _players.Add(PlayerModel.Create(teamId, userName, userId));
    }

    public void SetSecondPlayer(string userName, string userId)
    {
        var teamId = GetPlayerByTeam(TeamEnum.Cats) is null ? TeamEnum.Cats : TeamEnum.Mouse;
        _players.Add(PlayerModel.Create(teamId, userName, userId));
    }

    public void Start()
    {
        var mousePlayer = GetPlayerByTeam(TeamEnum.Mouse)
            ?? throw new InvalidOperationException("Mouse player does not exist");

        mousePlayer.IsTheirTurn = true;
        StartedTimestamp = _timeProvider.GetTimestamp();
        LastActivityTimestamp = StartedTimestamp.Value;

        RecalculateFiguresCanMoveToPositions();
    }

    public PlayerModel? GetPlayerByUserId(string userId)
    {
        foreach (var player in _players)
        {
            if (string.Equals(player.UserId, userId, StringComparison.Ordinal))
            {
                return player;
            }
        }

        return null;
    }

    public int IndexOfPlayer(PlayerModel player)
    {
        return _players.IndexOf(player);
    }

    public PlayerModel? GetOpponentPlayer(PlayerModel player)
    {
        foreach (var opponent in _players)
        {
            if (!ReferenceEquals(opponent, player))
            {
                return opponent;
            }
        }

        return null;
    }

    public static FigureModel? GetPlayerFigure(PlayerModel player, int figureId)
    {
        foreach (var figure in player.Figures)
        {
            if (figure.Id == figureId)
            {
                return figure;
            }
        }

        return null;
    }

    /// <summary>
    /// Every figure's legal moves, from one 64-bit occupancy mask: one bit per square, so a
    /// square test is a single AND and the whole board fits in a register.
    /// </summary>
    public void RecalculateFiguresCanMoveToPositions()
    {
        ulong occupiedSquares = 0;
        foreach (var player in _players)
        {
            foreach (var figure in player.Figures)
            {
                if (figure.Position.TryGetSquareMask(out var mask))
                {
                    occupiedSquares |= mask;
                }
            }
        }

        foreach (var player in _players)
        {
            foreach (var figure in player.Figures)
            {
                var moves = figure.CanMoveToPositions;
                moves.Clear();

                var (rowIndex, columnIndex) = figure.Position;

                // Every figure moves diagonally; only the mouse may also move up the board.
                if (figure.TypeId == FigureTypeEnum.Mouse)
                {
                    AddMoveIfSquareIsFree(moves, rowIndex - 1, columnIndex - 1, occupiedSquares);
                    AddMoveIfSquareIsFree(moves, rowIndex - 1, columnIndex + 1, occupiedSquares);
                }

                AddMoveIfSquareIsFree(moves, rowIndex + 1, columnIndex - 1, occupiedSquares);
                AddMoveIfSquareIsFree(moves, rowIndex + 1, columnIndex + 1, occupiedSquares);
            }
        }
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
        return GetWinnerPlayer() is not null;
    }

    public PlayerModel? GetWinnerPlayer()
    {
        foreach (var player in _players)
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
        foreach (var player in _players)
        {
            player.IsTheirTurn = !player.IsTheirTurn;
        }
    }

    public bool IsWaitingForSecondPlayer()
    {
        return StartedTimestamp is null && _players.Count == 1;
    }

    public bool IsGameInProgress()
    {
        return StartedTimestamp.HasValue && !IsGameOver();
    }

    public bool IsPasswordProtected()
    {
        return !string.IsNullOrWhiteSpace(Password);
    }

    public bool HasAnyPlayerLeft()
    {
        foreach (var player in _players)
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
        if (!IsGameOver() || HasAnyPlayerLeft())
        {
            return false;
        }

        foreach (var player in _players)
        {
            if (!player.WantsToRematch)
            {
                return false;
            }
        }

        return true;
    }

    public void PlayerWantsToRematch(PlayerModel player)
    {
        player.WantsToRematch = true;
        Touch();
    }

    public void PlayerLeft(PlayerModel playerWhoLeft)
    {
        playerWhoLeft.HasUserLeftTheGame = true;
        Touch();

        if (IsGameInProgress())
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

        Finish();
    }

    /// <summary>Appends to the feed, dropping the oldest entry once it is full.</summary>
    public void AddToFeed(GameFeedMessage message)
    {
        if (_feed.Count == MaxFeedLength)
        {
            _feed.Dequeue();
        }

        _feed.Enqueue(message);
        Touch();
    }

    /// <summary>
    /// The players as everyone in the game sees them, copied so the messages that carry them can
    /// be serialized after the lock is released.
    /// </summary>
    public PlayerStatusModel[] CreatePlayerSnapshots()
    {
        var snapshots = new PlayerStatusModel[_players.Count];
        for (var playerIndex = 0; playerIndex < snapshots.Length; playerIndex++)
        {
            var player = _players[playerIndex];
            var figures = new FigureStatusModel[player.Figures.Length];
            for (var figureIndex = 0; figureIndex < figures.Length; figureIndex++)
            {
                var figure = player.Figures[figureIndex];
                figures[figureIndex] = new FigureStatusModel(figure.Id, figure.TypeId, figure.Position, [.. figure.CanMoveToPositions]);
            }

            snapshots[playerIndex] = new PlayerStatusModel(
                player.Name,
                player.IsTheirTurn,
                player.TeamId,
                player.IsWinner,
                figures,
                player.HasUserLeftTheGame,
                player.WantsToRematch);
        }

        return snapshots;
    }

    public void Touch()
    {
        LastActivityTimestamp = _timeProvider.GetTimestamp();
    }

    private void CheckForGameOver()
    {
        var mousePlayer = GetPlayerByTeam(TeamEnum.Mouse)
            ?? throw new InvalidOperationException("Mouse player does not exist");
        var catsPlayer = GetPlayerByTeam(TeamEnum.Cats)
            ?? throw new InvalidOperationException("Cats player does not exist");

        if (mousePlayer.Figures[0].Position.RowIndex == 0)
        {
            mousePlayer.IsWinner = true;
        }
        else
        {
            // The turn has not passed yet, so the player about to move next is whoever is not on
            // turn now. If they are stuck, the player who just moved has won.
            var nextTurnPlayer = mousePlayer.IsTheirTurn ? catsPlayer : mousePlayer;

            if (!CanMoveAnyFigure(nextTurnPlayer))
            {
                var currentTurnPlayer = nextTurnPlayer == catsPlayer ? mousePlayer : catsPlayer;
                currentTurnPlayer.IsWinner = true;
            }
        }

        if (IsGameOver())
        {
            Finish();
        }
    }

    private void Finish()
    {
        foreach (var player in _players)
        {
            player.IsTheirTurn = false;
        }

        FinishedTimestamp = _timeProvider.GetTimestamp();
        LastActivityTimestamp = FinishedTimestamp.Value;
    }

    private static bool CanMoveAnyFigure(PlayerModel player)
    {
        foreach (var figure in player.Figures)
        {
            if (figure.CanMoveToPositions.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private PlayerModel? GetPlayerByTeam(TeamEnum teamId)
    {
        foreach (var player in _players)
        {
            if (player.TeamId == teamId)
            {
                return player;
            }
        }

        return null;
    }

    private static void AddMoveIfSquareIsFree(List<BoardPosition> moves, int rowIndex, int columnIndex, ulong occupiedSquares)
    {
        var target = new BoardPosition(rowIndex, columnIndex);
        if (target.TryGetSquareMask(out var mask) && (occupiedSquares & mask) == 0)
        {
            moves.Add(target);
        }
    }
}
