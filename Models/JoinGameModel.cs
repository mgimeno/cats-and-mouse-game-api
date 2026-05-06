
namespace CatsAndMouseGame.Models
{
    public class JoinGameModel
    {
        public string GameId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? GamePassword { get; set; }
    }
}
