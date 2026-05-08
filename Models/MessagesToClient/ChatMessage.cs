using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Interfaces;

namespace CatsAndMouseApi.Models.MessagesToClient
{
    public class ChatMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.ChatMessage;
        public bool IsMessageForChat { get; } = true;
        public string GameId { get; set; } = string.Empty;

        public ChatLineModel ChatLine { get; set; } = new();
    }
}
