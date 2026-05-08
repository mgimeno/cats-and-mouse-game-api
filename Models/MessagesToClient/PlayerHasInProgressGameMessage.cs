using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Interfaces;

namespace CatsAndMouseApi.Models.MessagesToClient
{
    public class PlayerHasInProgressGameMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.PlayerHasInProgressGame;
        public bool IsMessageForChat { get; } = false;
        public bool HasInProgressGame { get; set; }
    }
}
