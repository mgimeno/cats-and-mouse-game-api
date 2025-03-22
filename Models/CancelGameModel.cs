namespace CatsAndMouseGame.Models
{
    public class CancelGameModel 
    {
        public string GameId { get; set; }
        public string UserId { get; set; }
        public bool SendAwaitingGamesToAllClients { get; set; } = true;
    }
}
