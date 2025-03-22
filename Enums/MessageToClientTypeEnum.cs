namespace CatsAndMouseGame.Enums
{
    public enum MessageToClientTypeEnum
    {
        GameList = 1,
        GameStart = 2,
        GameStatus = 3,
        ChatMessage = 4,
        PlayerHasInProgressGame = 5,
        PlayerHasLeftGame = 6,
        PlayerWantsToRematch = 7,
        PlayerHasSurrendered = 8,
        PlayerOnlyConnectionStatusChanged = 9
    }
}
