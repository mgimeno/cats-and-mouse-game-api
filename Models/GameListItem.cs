using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models
{
    public class GameListItem
    {
        public string GameId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public TeamEnum TeamId { get; set; }
        public bool IsPasswordProtected { get; set; }
    }
}
