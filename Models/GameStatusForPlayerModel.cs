using System.Collections.Generic;

namespace CatsAndMouseGame.Models
{
    public class GameStatusForPlayerModel
    {
        public string GameId { get; set; }
        public List<PlayerModel> Players { get; set; }
        public int MyPlayerIndex { get; set; }
    }
}
