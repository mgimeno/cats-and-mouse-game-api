using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Hubs;

namespace CatsAndMouseGame.Models
{
    public class PlayerHasLeftGameMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.PlayerHasLeftGame;
        public bool IsMessageForChat { get; } = true;
        public string GameId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public TeamEnum TeamId { get; set; }
    }
}
