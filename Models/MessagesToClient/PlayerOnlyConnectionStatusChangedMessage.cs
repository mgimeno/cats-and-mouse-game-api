using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Hubs;

namespace CatsAndMouseGame.Models
{
    public class PlayerOnlyConnectionStatusChangedMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.PlayerOnlyConnectionStatusChanged;
        public bool IsMessageForChat { get; } = true;
        public string GameId { get; set; }
        public string UserName { get; set; }
        public TeamEnum TeamId { get; set; }
        public bool IsConnected { get; set; }
    }
}
