using CatsAndMouseGame.Enums;

namespace CatsAndMouseGame.Models
{
    public class CatsPlayerModel : PlayerModel
    {
        public CatsPlayerModel()
        {
            this.TeamId = TeamEnum.Cats;

            var cat1 = new CatModel
            {
                Id = 1,
                Position = new FigurePositionModel
                {
                    RowIndex = 0,
                    ColumnIndex = 1
                }
            };
            this.Figures.Add(cat1);

            var cat2 = new CatModel
            {
                Id = 2,
                Position = new FigurePositionModel
                {
                    RowIndex = 0,
                    ColumnIndex = 3
                }
            };
            this.Figures.Add(cat2);

            var cat3 = new CatModel
            {
                Id = 3,
                Position = new FigurePositionModel
                {
                    RowIndex = 0,
                    ColumnIndex = 5
                }
            };
            this.Figures.Add(cat3);

            var cat4 = new CatModel
            {
                Id = 4,
                Position = new FigurePositionModel
                {
                    RowIndex = 0,
                    ColumnIndex = 7
                }
            };
            this.Figures.Add(cat4);

        }


    }
}
