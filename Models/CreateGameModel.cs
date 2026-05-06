using CatsAndMouseGame.Enums;

namespace CatsAndMouseGame.Models
{
    public class CreateGameModel
    {
        public string UserName { get; set; } = string.Empty;
        public TeamEnum TeamId { get; set; }
        public string? GamePassword { get; set; }

    }
}
