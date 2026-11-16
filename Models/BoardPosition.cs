namespace CatsAndMouseApi.Models;

/// <summary>
/// A square on the board, serialized as <c>{ "rowIndex": r, "columnIndex": c }</c>. A value type,
/// so a figure's position and its possible moves cost no allocation of their own.
/// </summary>
public readonly record struct BoardPosition(int RowIndex, int ColumnIndex)
{
    public const int BoardSize = 8;

    /// <summary>
    /// The square's bit in a 64-bit board mask, one bit per square. False for a position off the
    /// board, which has no bit.
    /// </summary>
    internal bool TryGetSquareMask(out ulong mask)
    {
        if ((uint)RowIndex >= BoardSize || (uint)ColumnIndex >= BoardSize)
        {
            mask = 0;
            return false;
        }

        mask = 1UL << ((RowIndex * BoardSize) + ColumnIndex);
        return true;
    }
}
