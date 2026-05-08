namespace CatsAndMouseApi.Models
{
    public class GameStatusForPlayerModel
    {
        public string GameId { get; set; } = string.Empty;
        public List<PlayerModel> Players { get; set; } = [];
        public int MyPlayerIndex { get; set; }
    }
}
