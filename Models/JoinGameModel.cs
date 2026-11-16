namespace CatsAndMouseApi.Models;

public sealed class JoinGameModel
{
    public string GameId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string? GamePassword { get; init; }
}
