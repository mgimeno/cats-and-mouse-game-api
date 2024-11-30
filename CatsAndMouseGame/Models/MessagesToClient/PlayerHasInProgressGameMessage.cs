using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Hubs;

namespace CatsAndMouseGame.Models
{
    public class PlayerHasInProgressGameMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.PlayerHasInProgressGame;
        public bool IsMessageForChat { get; } = false;
        public bool HasInProgressGame { get; set; }
    }
}
