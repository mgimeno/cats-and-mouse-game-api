using CatsAndMouseApi.Models.MessagesToClient;

namespace CatsAndMouseApi.Services;

public enum ClientMessageAudience
{
    /// <summary>Exactly the listed connections.</summary>
    Connections,

    /// <summary>Every connection to the hub except the listed ones.</summary>
    AllExcept
}

/// <summary>A message to push once the game lock is released. A value type: no allocation per message.</summary>
public readonly record struct ClientMessage(
    ClientMessageAudience Audience,
    IReadOnlyList<string> ConnectionIds,
    string MethodName,
    IMessageToClient Message)
{
    public static ClientMessage To(IReadOnlyList<string> connectionIds, string methodName, IMessageToClient message)
    {
        return new ClientMessage(ClientMessageAudience.Connections, connectionIds, methodName, message);
    }

    public static ClientMessage ToAllExcept(IReadOnlyList<string> excludedConnectionIds, string methodName, IMessageToClient message)
    {
        return new ClientMessage(ClientMessageAudience.AllExcept, excludedConnectionIds, methodName, message);
    }
}
