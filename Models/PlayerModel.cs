using System.Collections.Immutable;
using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models;

public sealed class PlayerModel
{
    private PlayerModel(TeamEnum teamId, string name, string userId, ImmutableArray<FigureModel> figures)
    {
        TeamId = teamId;
        Name = name;
        UserId = userId;
        Figures = figures;
    }

    /// <summary>
    /// The player's credential: whoever registers a connection with it acts as this player. It is
    /// never sent to anyone else.
    /// </summary>
    public string UserId { get; }
    public string Name { get; }
    public TeamEnum TeamId { get; }
    public ImmutableArray<FigureModel> Figures { get; }

    public bool IsTheirTurn { get; set; }
    public bool IsWinner { get; set; }
    public bool HasUserLeftTheGame { get; set; }
    public bool WantsToRematch { get; set; }

    /// <summary>A player with their team's figures on their starting squares.</summary>
    public static PlayerModel Create(TeamEnum teamId, string name, string userId)
    {
        return teamId switch
        {
            TeamEnum.Cats => new PlayerModel(teamId, name, userId,
            [
                new FigureModel(1, FigureTypeEnum.Cat, new BoardPosition(0, 1)),
                new FigureModel(2, FigureTypeEnum.Cat, new BoardPosition(0, 3)),
                new FigureModel(3, FigureTypeEnum.Cat, new BoardPosition(0, 5)),
                new FigureModel(4, FigureTypeEnum.Cat, new BoardPosition(0, 7))
            ]),
            TeamEnum.Mouse => new PlayerModel(teamId, name, userId,
            [
                new FigureModel(0, FigureTypeEnum.Mouse, new BoardPosition(7, 4))
            ]),
            _ => throw new InvalidOperationException("Team is invalid")
        };
    }
}
