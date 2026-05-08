using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Interfaces;

namespace CatsAndMouseApi.Models.MessagesToClient
{
    public class PlayerOnlyConnectionStatusChangedMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.PlayerOnlyConnectionStatusChanged;
        public bool IsMessageForChat { get; } = true;
        public string GameId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public TeamEnum TeamId { get; set; }
        public bool IsConnected { get; set; }
    }
}
