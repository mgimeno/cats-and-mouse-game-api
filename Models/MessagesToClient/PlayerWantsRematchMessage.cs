using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Interfaces;

namespace CatsAndMouseApi.Models.MessagesToClient
{
    public class PlayerWantsRematchMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.PlayerWantsToRematch;
        public bool IsMessageForChat { get; } = true;
        public string GameId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public TeamEnum TeamId { get; set; }
    }
}
