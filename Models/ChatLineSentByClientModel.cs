namespace CatsAndMouseApi.Models;

public sealed class ChatLineSentByClientModel
{
    public string GameId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
