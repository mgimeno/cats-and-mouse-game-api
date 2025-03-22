using CatsAndMouseGame.Enums;

namespace CatsAndMouseGame.Models
{
    public class GameListItem
    {
        public string GameId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public TeamEnum TeamId { get; set; }
        public bool IsPasswordProtected { get; set; }
    }
}
