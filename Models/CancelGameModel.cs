namespace CatsAndMouseApi.Models;

public sealed class CancelGameModel
{
    public string GameId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public bool SendAwaitingGamesToAllClients { get; init; } = true;
}
