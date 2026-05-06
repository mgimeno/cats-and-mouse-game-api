using System.Collections.Generic;

namespace CatsAndMouseGame.Models
{
    public class GameStatusForPlayerModel
    {
        public string GameId { get; set; } = string.Empty;
        public List<PlayerModel> Players { get; set; } = new();
        public int MyPlayerIndex { get; set; }
    }
}
