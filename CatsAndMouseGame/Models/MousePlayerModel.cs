using CatsAndMouseGame.Enums;

namespace CatsAndMouseGame.Models
{
    public class MousePlayerModel : PlayerModel
    {

        public MousePlayerModel()
        {
            this.TeamId = TeamEnum.Mouse;

            var mouse = new MouseModel
            {
                Id = 0,
                Position = new FigurePositionModel
                {
                    RowIndex = 7,
                    ColumnIndex = 4
                }
            };

            this.Figures.Add(mouse);
        }


    }
}
