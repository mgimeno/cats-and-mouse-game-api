
namespace CatsAndMouseGame.Models
{
    public class JoinGameModel
    {
        public string GameId { get; set; }
        public string UserName { get; set; }
        public string GamePassword { get; set; } = null;
    }
}
