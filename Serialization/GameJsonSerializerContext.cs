using System.Text.Json.Serialization;
using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;

namespace CatsAndMouseApi.Serialization;

/// <summary>
/// Compile-time JSON metadata for every type that crosses the hub: the arguments clients send,
/// the value CreateGame returns, and every message pushed to clients. Native AOT has no
/// reflection-based serializer to fall back on, so a type missing here fails at runtime there;
/// the tests check the list against the hub.
/// </summary>
/// <remarks>
/// The options match the SignalR JSON protocol's own defaults (camelCase, case-insensitive reads),
/// which lets the serializer take the generated fast path instead of walking metadata.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
// Hub method arguments and results.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(CreateGameModel))]
[JsonSerializable(typeof(JoinGameModel))]
[JsonSerializable(typeof(MoveFigureModel))]
[JsonSerializable(typeof(CancelGameModel))]
[JsonSerializable(typeof(ChatLineSentByClientModel))]
[JsonSerializable(typeof(GameIdModel))]
[JsonSerializable(typeof(GameListItem))]
// Messages pushed to clients. They are sent as object, so each concrete type needs its own entry.
[JsonSerializable(typeof(GameListMessage))]
[JsonSerializable(typeof(GameStartMessage))]
[JsonSerializable(typeof(GameStatusMessage))]
[JsonSerializable(typeof(PlayerHasInProgressGameMessage))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(PlayerHasLeftGameMessage))]
[JsonSerializable(typeof(PlayerHasSurrenderedMessage))]
[JsonSerializable(typeof(PlayerOnlyConnectionStatusChangedMessage))]
[JsonSerializable(typeof(PlayerWantsRematchMessage))]
public sealed partial class GameJsonSerializerContext : JsonSerializerContext;
