namespace CatsAndMouseApi.Models.MessagesToClient;

/// <summary>
/// One entry of a game's feed: the messages a client replays, in order, when it reconnects to a
/// game in progress. A C# 15 union: a struct holding a single reference, so the feed stores no
/// wrapper objects, and a switch over it is exhaustive at compile time. Adding a case without
/// mapping it below is a build error, not a runtime "unknown message" failure.
/// </summary>
public union GameFeedMessage(
    ChatMessage,
    PlayerHasLeftGameMessage,
    PlayerHasSurrenderedMessage,
    PlayerOnlyConnectionStatusChangedMessage,
    PlayerWantsRematchMessage)
{
    /// <summary>The client method this entry is delivered to.</summary>
    public string ClientMethodName => this switch
    {
        ChatMessage => ClientMethodNames.ChatMessage,
        PlayerHasLeftGameMessage => ClientMethodNames.PlayerHasLeftGame,
        PlayerHasSurrenderedMessage => ClientMethodNames.PlayerHasSurrendered,
        PlayerOnlyConnectionStatusChangedMessage => ClientMethodNames.PlayerOnlyConnectionStatusChanged,
        PlayerWantsRematchMessage => ClientMethodNames.PlayerWantsRematch,
        null => throw new InvalidOperationException("The game feed holds an empty entry.")
    };

    /// <summary>The entry itself, which is what goes on the wire: never the union wrapper.</summary>
    public IMessageToClient Message => (IMessageToClient)(Value ?? throw new InvalidOperationException("The game feed holds an empty entry."));
}
