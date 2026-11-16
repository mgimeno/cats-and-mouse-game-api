using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models.MessagesToClient;

// Everything the hub pushes to clients. Immutable records built under the game lock and sent
// after it is released, so a message can never observe a game changing underneath it. Their JSON
// shape is the UI's contract: property names are camel-cased by the SignalR JSON protocol, and
// every message carries its typeId.

public interface IMessageToClient
{
    MessageToClientTypeEnum TypeId { get; }
}

public sealed record GameListMessage(GameListItem[] GameList) : IMessageToClient
{
    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.GameList;
}

public sealed record GameStartMessage : IMessageToClient
{
    public static readonly GameStartMessage Instance = new();

    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.GameStart;
}

public sealed record GameStatusMessage(GameStatusForPlayerModel GameStatus) : IMessageToClient
{
    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.GameStatus;
}

public sealed record PlayerHasInProgressGameMessage(bool HasInProgressGame) : IMessageToClient
{
    public static readonly PlayerHasInProgressGameMessage Yes = new(true);
    public static readonly PlayerHasInProgressGameMessage No = new(false);

    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.PlayerHasInProgressGame;
}

public sealed record ChatMessage(string GameId, ChatLineModel ChatLine) : IMessageToClient
{
    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.ChatMessage;
}

public sealed record PlayerHasLeftGameMessage(string GameId, string UserName, TeamEnum TeamId) : IMessageToClient
{
    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.PlayerHasLeftGame;
}

public sealed record PlayerHasSurrenderedMessage(string GameId, string UserName, TeamEnum TeamId) : IMessageToClient
{
    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.PlayerHasSurrendered;
}

public sealed record PlayerOnlyConnectionStatusChangedMessage(string GameId, string UserName, TeamEnum TeamId, bool IsConnected) : IMessageToClient
{
    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.PlayerOnlyConnectionStatusChanged;
}

public sealed record PlayerWantsRematchMessage(string GameId, string UserName, TeamEnum TeamId) : IMessageToClient
{
    public MessageToClientTypeEnum TypeId => MessageToClientTypeEnum.PlayerWantsToRematch;
}
