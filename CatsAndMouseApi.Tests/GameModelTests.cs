using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;

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
    public void Figures_cannot_move_onto_an_occupied_square()
    {
        var game = StartedGame();
        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);

        cats.Figures[0].ChangePosition(6, 3);
        game.RecalculateFiguresCanMoveToPositions();

        Assert.Equal([new BoardPosition(6, 5)], mouse.Figures[0].CanMoveToPositions);
        Assert.DoesNotContain(cats.Figures[0].CanMoveToPositions, p => p == new BoardPosition(7, 4));
    }

    [Fact]
    public void Edge_figures_only_get_on_board_moves()
    {
        var game = StartedGame();
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);

        var lastCatMoves = cats.Figures.Single(f => f.Id == 4).CanMoveToPositions;

        Assert.Equal([new BoardPosition(1, 6)], lastCatMoves);
    }

    [Fact]
    public void Mouse_wins_when_it_reaches_top_row()
    {
        var game = StartedGame();
        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);

        game.Move(mouse.Figures[0], rowIndex: 0, columnIndex: 1);

        Assert.True(game.IsGameOver());
        Assert.Same(mouse, game.GetWinnerPlayer());
        Assert.NotNull(game.FinishedTimestamp);
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
        Assert.NotNull(game.FinishedTimestamp);
    }

    [Fact]
    public void Mouse_wins_when_its_move_leaves_cats_with_no_moves()
    {
        var game = StartedGame();
        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);

        // Every cat on the bottom row, where it has nowhere left to go.
        cats.Figures[0].ChangePosition(7, 0);
        cats.Figures[1].ChangePosition(7, 2);
        cats.Figures[2].ChangePosition(7, 4);
        cats.Figures[3].ChangePosition(7, 6);
        mouse.Figures[0].ChangePosition(4, 3);
        game.RecalculateFiguresCanMoveToPositions();

        game.Move(mouse.Figures[0], rowIndex: 3, columnIndex: 2);

        Assert.True(game.IsGameOver());
        Assert.Same(mouse, game.GetWinnerPlayer());
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
        var game = new GameModel("game-1", null, TimeProvider.System);
        game.SetFirstPlayer(TeamEnum.Cats, "Cats", "cats-user");

        var exception = Assert.Throws<InvalidOperationException>(() => game.Start());
        Assert.Equal("Mouse player does not exist", exception.Message);
    }

    [Fact]
    public void RecalculateFiguresCanMoveToPositions_ignores_out_of_bounds_current_positions()
    {
        var game = StartedGame();
        var cats = game.Players.Single(p => p.TeamId == TeamEnum.Cats);
        cats.Figures[0].ChangePosition(rowIndex: -1, columnIndex: 9);

        var exception = Record.Exception(game.RecalculateFiguresCanMoveToPositions);

        Assert.Null(exception);
    }

    [Fact]
    public void Feed_keeps_only_the_latest_entries_in_order()
    {
        var game = StartedGame();

        for (var i = 0; i < GameModel.MaxFeedLength + 5; i++)
        {
            game.AddToFeed(new ChatMessage(game.Id, new ChatLineModel("Cats", TeamEnum.Cats, $"line {i}")));
        }

        Assert.Equal(GameModel.MaxFeedLength, game.Feed.Count);
        var first = Assert.IsType<ChatMessage>(game.Feed.First().Message);
        var last = Assert.IsType<ChatMessage>(game.Feed.Last().Message);
        Assert.Equal("line 5", first.ChatLine.Message);
        Assert.Equal($"line {GameModel.MaxFeedLength + 4}", last.ChatLine.Message);
    }

    [Fact]
    public void Player_snapshots_are_independent_of_later_moves()
    {
        var game = StartedGame();
        var mouse = game.Players.Single(p => p.TeamId == TeamEnum.Mouse);

        var snapshots = game.CreatePlayerSnapshots();
        game.Move(mouse.Figures[0], rowIndex: 6, columnIndex: 3);

        var mouseSnapshot = snapshots.Single(p => p.TeamId == TeamEnum.Mouse);
        Assert.Equal(new BoardPosition(7, 4), mouseSnapshot.Figures[0].Position);
        Assert.Contains(new BoardPosition(6, 3), mouseSnapshot.Figures[0].CanMoveToPositions);
    }

    private static GameModel StartedGame()
    {
        var game = new GameModel("game-1", null, TimeProvider.System);
        game.SetFirstPlayer(TeamEnum.Cats, "Cats", "cats-user");
        game.SetSecondPlayer("Mouse", "mouse-user");
        game.Start();
        return game;
    }
}
