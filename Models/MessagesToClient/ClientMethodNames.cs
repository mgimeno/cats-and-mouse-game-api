namespace CatsAndMouseApi.Models.MessagesToClient;

/// <summary>The client-side handler names the UI registers with <c>connection.on(...)</c>.</summary>
public static class ClientMethodNames
{
    public const string GameList = "GameList";
    public const string GameStart = "GameStart";
    public const string GameStatus = "GameStatus";
    public const string HasInProgressGame = "HasInProgressGame";
    public const string ChatMessage = "ChatMessage";
    public const string PlayerHasLeftGame = "PlayerHasLeftGame";
    public const string PlayerHasSurrendered = "PlayerHasSurrendered";
    public const string PlayerOnlyConnectionStatusChanged = "PlayerOnlyConnectionStatusChanged";
    public const string PlayerWantsRematch = "PlayerWantsRematch";
}
