using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models.MessagesToClient;

// Point-in-time copies of game state for the messages above. Arrays rather than lists or
// interfaces: the serializer writes an array without allocating an enumerator.

/// <param name="UserId">
/// Empty unless the list goes to the game's own creator: a user id is the only credential
/// RegisterConnection asks for, so it never reaches anyone else. The UI compares it with its own
/// id to recognise the game it created.
/// </param>
public sealed record GameListItem(string GameId, string UserId, string UserName, TeamEnum TeamId, bool IsPasswordProtected);

public sealed record ChatLineModel(string UserName, TeamEnum TeamId, string Message);

public sealed record GameStatusForPlayerModel(string GameId, PlayerStatusModel[] Players, int MyPlayerIndex);

/// <summary>A player as both players see them. Deliberately without the user id.</summary>
public sealed record PlayerStatusModel(
    string Name,
    bool IsTheirTurn,
    TeamEnum TeamId,
    bool IsWinner,
    FigureStatusModel[] Figures,
    bool HasUserLeftTheGame,
    bool WantsToRematch);

public sealed record FigureStatusModel(int Id, FigureTypeEnum TypeId, BoardPosition Position, BoardPosition[] CanMoveToPositions);
