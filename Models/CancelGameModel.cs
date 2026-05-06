namespace CatsAndMouseGame.Models
{
    public class CancelGameModel 
    {
        public string GameId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public bool SendAwaitingGamesToAllClients { get; set; } = true;
    }
}
