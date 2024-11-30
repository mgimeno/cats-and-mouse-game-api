using CatsAndMouseGame.Enums;
using System.Collections.Generic;

namespace CatsAndMouseGame.Models
{
    public class PlayerModel
    {
        public string UserId { get; set; } = null;
        public string Name { get; set; } = null;
        public bool IsTheirTurn { get; set; } = false;
        public TeamEnum TeamId { get; set; }
        public bool IsWinner { get; set; } = false;

        public List<FigureModel> Figures { get; set; }

        public bool HasUserLeftTheGame { get; set; } = false;
        public bool WantsToRematch { get; set; } = false;

        public PlayerModel() {
            this.Figures = new List<FigureModel>();
        }
        
    }
}
