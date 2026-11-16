using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models;

public sealed class FigureModel(int id, FigureTypeEnum typeId, BoardPosition position)
{
    public int Id { get; } = id;
    public FigureTypeEnum TypeId { get; } = typeId;
    public BoardPosition Position { get; private set; } = position;

    /// <summary>Recalculated by <see cref="GameModel"/> after every move. A figure has at most four.</summary>
    public List<BoardPosition> CanMoveToPositions { get; } = [with(capacity: 4)];

    public void ChangePosition(int rowIndex, int columnIndex)
    {
        Position = new BoardPosition(rowIndex, columnIndex);
    }

    public bool CanMoveTo(int rowIndex, int columnIndex)
    {
        return CanMoveToPositions.Contains(new BoardPosition(rowIndex, columnIndex));
    }
}
