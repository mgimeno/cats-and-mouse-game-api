using CatsAndMouseGame.Enums;
using CatsAndMouseGame.Hubs;

namespace CatsAndMouseGame.Models
{
    public class ChatMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.ChatMessage;
        public bool IsMessageForChat { get; } = true;
        public string GameId { get; set; } = string.Empty;

        public ChatLineModel ChatLine { get; set; } = new();
    }
}
