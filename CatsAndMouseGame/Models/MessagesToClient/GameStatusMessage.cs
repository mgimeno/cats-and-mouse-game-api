using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Hubs;

namespace CatsAndMouseGame.Models
{
    public class GameStatusMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.GameStatus;
        public bool IsMessageForChat { get; } = false;
        public GameStatusForPlayerModel GameStatus { get; set; }
    }
}
