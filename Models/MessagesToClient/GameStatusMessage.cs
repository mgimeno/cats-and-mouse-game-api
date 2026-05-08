using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Interfaces;

namespace CatsAndMouseApi.Models.MessagesToClient
{
    public class GameStatusMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.GameStatus;
        public bool IsMessageForChat { get; } = false;
        public GameStatusForPlayerModel GameStatus { get; set; } = new();
    }
}
