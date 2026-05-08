using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Interfaces
{
    public interface IMessageToClient
    {
        MessageToClientTypeEnum TypeId { get; }
        bool IsMessageForChat { get; }
    }
}
