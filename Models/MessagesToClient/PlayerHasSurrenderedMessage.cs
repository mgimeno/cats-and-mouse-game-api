using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Hubs;

namespace CatsAndMouseGame.Models
{
    public class PlayerHasSurrenderedMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.PlayerHasSurrendered;
        public bool IsMessageForChat { get; } = true;
        public string GameId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public TeamEnum TeamId { get; set; }
    }
}
