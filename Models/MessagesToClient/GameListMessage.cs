using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Interfaces;

namespace CatsAndMouseApi.Models.MessagesToClient
{
    public class GameListMessage : IMessageToClient
    {
        public MessageToClientTypeEnum TypeId { get; } = MessageToClientTypeEnum.GameList;
        public bool IsMessageForChat { get; } = false;
        public List<GameListItem> GameList { get; set; } = [];
    }
}
