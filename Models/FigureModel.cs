using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models
{
    public class FigureModel
    {
        public int Id { get; set; }
        public FigurePositionModel Position { get; set; }
        public FigureTypeEnum TypeId { get; set; }
        public List<FigurePositionModel> CanMoveToPositions { get; set; }

        public FigureModel()
        {
            this.Position = new FigurePositionModel();
            this.CanMoveToPositions = new List<FigurePositionModel>(4);
        }

        public void ChangePosition(int rowIndex, int columnIndex)
        {
            this.Position.RowIndex = rowIndex;
            this.Position.ColumnIndex = columnIndex;
        }

        public void AddCanMoveToPosition(int rowIndex, int columnIndex)
        {
            this.CanMoveToPositions.Add(new FigurePositionModel
            {
                RowIndex = rowIndex,
                ColumnIndex = columnIndex
            });
        }

    }
}
