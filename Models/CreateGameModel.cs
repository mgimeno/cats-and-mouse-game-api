using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models;

public sealed class CreateGameModel
{
    public string UserName { get; init; } = string.Empty;
    public TeamEnum TeamId { get; init; }
    public string? GamePassword { get; init; }
}
