using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Models;

namespace CatsAndMouseApi.Tests;

public sealed class GameModelTests
{
    [Fact]
    public void Start_sets_mouse_turn_and_initial_moves()
    {
        var game = StartedGame();

        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);

        Assert.True(mouse.IsTheirTurn);
        Assert.False(cats.IsTheirTurn);
        Assert.Contains(mouse.Figures[0].CanMoveToPositions, p => p.RowIndex == 6 && p.ColumnIndex == 3);
        Assert.Contains(mouse.Figures[0].CanMoveToPositions, p => p.RowIndex == 6 && p.ColumnIndex == 5);
        Assert.DoesNotContain(mouse.Figures[0].CanMoveToPositions, p => p.RowIndex == 8);
    }

    [Fact]
    public void Cats_can_only_move_down_the_board()
    {
        var game = StartedGame();
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);

        var firstCatMoves = cats.Figures.Single(f => f.Id == 1).CanMoveToPositions;

        Assert.Equal(2, firstCatMoves.Count);
        Assert.All(firstCatMoves, move => Assert.Equal(1, move.RowIndex));
        Assert.Contains(firstCatMoves, p => p.ColumnIndex == 0);
        Assert.Contains(firstCatMoves, p => p.ColumnIndex == 2);
    }

    [Fact]
    public void Mouse_wins_when_it_reaches_top_row()
    {
        var game = StartedGame();
        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);

        game.Move(mouse.Figures[0], rowIndex: 0, columnIndex: 1);

        Assert.True(game.IsGameOver());
        Assert.Same(mouse, game.GetWinnerPlayer());
        Assert.NotNull(game.DateFinished);
        Assert.All(game.Players, player => Assert.False(player.IsTheirTurn));
    }

    [Fact]
    public void Cats_win_when_their_move_leaves_mouse_with_no_moves()
    {
        var game = StartedGame();
        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);

        mouse.IsTheirTurn = false;
        cats.IsTheirTurn = true;
        mouse.Figures[0].ChangePosition(5, 2);
        cats.Figures[0].ChangePosition(4, 1);
        cats.Figures[1].ChangePosition(4, 3);
        cats.Figures[2].ChangePosition(6, 1);
        cats.Figures[3].ChangePosition(5, 4);
        game.RecalculateFiguresCanMoveToPositions();

        game.Move(cats.Figures[3], rowIndex: 6, columnIndex: 3);

        Assert.True(game.IsGameOver());
        Assert.Same(cats, game.GetWinnerPlayer());
        Assert.NotNull(game.DateFinished);
    }

    [Fact]
    public void Surrender_marks_opponent_as_winner_and_clears_turns()
    {
        var game = StartedGame();
        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);

        game.PlayerSurrenders(mouse);

        Assert.True(cats.IsWinner);
        Assert.False(mouse.IsWinner);
        Assert.True(game.IsGameOver());
        Assert.All(game.Players, player => Assert.False(player.IsTheirTurn));
    }

    [Fact]
    public void Start_requires_both_teams()
    {
        var game = new GameModel();
        game.SetFirstPlayer(TeamEnum.Cats, "Cats", "cats-user");

        var exception = Assert.Throws<InvalidOperationException>(() => game.Start());
        Assert.Equal("Mouse player does not exist", exception.Message);
    }

    private static GameModel StartedGame()
    {
        var game = new GameModel();
        game.SetFirstPlayer(TeamEnum.Cats, "Cats", "cats-user");
        game.SetSecondPlayer("Mouse", "mouse-user");
        game.Start();
        return game;
    }
}
