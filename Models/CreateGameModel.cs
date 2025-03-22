using CatsAndMouseGame.Enums;

namespace CatsAndMouseGame.Models
{
    public class CreateGameModel
    {
        public string UserName { get; set; }
        public TeamEnum TeamId { get; set; }
        public string GamePassword { get; set; } = null;

    }
}
