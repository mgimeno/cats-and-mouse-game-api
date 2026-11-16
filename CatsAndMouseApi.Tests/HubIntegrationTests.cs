using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace CatsAndMouseApi.Tests;

/// <summary>
/// The real app (Program, its JSON setup, CORS and routing) in memory, driven by the real
/// SignalR client over WebSockets, the way the browser drives it. Payloads are read back as raw
/// JSON so the assertions are about the wire format the UI depends on.
/// </summary>
public sealed class HubIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Status_endpoint_reports_ready()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Cats & Mouse API is ready", body, StringComparison.Ordinal);
        Assert.Contains("Environment: DEVELOPMENT", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:4200", true)]
    [InlineData("https://evil.example", false)]
    public async Task Hub_negotiation_honours_the_cors_allow_list(string origin, bool isAllowed)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/gameHub/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", origin);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(isAllowed, response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) && values.Contains(origin));
    }

    [Fact]
    public async Task Two_players_can_play_chat_finish_and_rematch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var catsUserId = $"cats-{Guid.NewGuid()}";
        var mouseUserId = $"mouse-{Guid.NewGuid()}";
        await using var cats = await HubClient.ConnectAsync(factory.Server, cancellationToken);
        await using var mouse = await HubClient.ConnectAsync(factory.Server, cancellationToken);

        await cats.Connection.InvokeAsync("RegisterConnection", catsUserId, cancellationToken);
        await mouse.Connection.InvokeAsync("RegisterConnection", mouseUserId, cancellationToken);
        Assert.False((await cats.NextAsync("HasInProgressGame")).GetProperty("hasInProgressGame").GetBoolean());

        // The creator gets their own id back; the other player's list never carries it.
        var created = await cats.Connection.InvokeAsync<JsonElement>(
            "CreateGame",
            new CreateGameModel { UserName = "Cats", TeamId = TeamEnum.Cats },
            cancellationToken);
        var gameId = created.GetProperty("gameId").GetString()!;
        Assert.Equal(catsUserId, created.GetProperty("userId").GetString());
        Assert.False(created.GetProperty("isPasswordProtected").GetBoolean());

        var listSeenByMouse = await mouse.NextAsync("GameList", list => list.GetProperty("gameList").GetArrayLength() > 0);
        var listedGame = listSeenByMouse.GetProperty("gameList").EnumerateArray().Single(game => game.GetProperty("gameId").GetString() == gameId);
        Assert.Equal(string.Empty, listedGame.GetProperty("userId").GetString());
        Assert.DoesNotContain(catsUserId, listSeenByMouse.GetRawText(), StringComparison.Ordinal);

        await mouse.Connection.InvokeAsync("JoinGame", new JoinGameModel { GameId = gameId, UserName = "Mouse" }, cancellationToken);
        await cats.NextAsync("GameStart");
        await mouse.NextAsync("GameStart");
        var mouseStatus = (await mouse.NextAsync("GameStatus")).GetProperty("gameStatus");
        Assert.Equal(1, mouseStatus.GetProperty("myPlayerIndex").GetInt32());
        Assert.True(mouseStatus.GetProperty("players")[1].GetProperty("isTheirTurn").GetBoolean());
        Assert.DoesNotContain(catsUserId, mouseStatus.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(mouseUserId, mouseStatus.GetRawText(), StringComparison.Ordinal);
        await cats.NextAsync("GameStatus");

        await mouse.Connection.InvokeAsync("Move", new MoveFigureModel { FigureId = 0, RowIndex = 6, ColumnIndex = 3 }, cancellationToken);
        var catsStatus = (await cats.NextAsync("GameStatus")).GetProperty("gameStatus");
        Assert.True(catsStatus.GetProperty("players")[0].GetProperty("isTheirTurn").GetBoolean());
        Assert.Equal(6, catsStatus.GetProperty("players")[1].GetProperty("figures")[0].GetProperty("position").GetProperty("rowIndex").GetInt32());
        await mouse.NextAsync("GameStatus");

        var error = await Assert.ThrowsAsync<HubException>(() => mouse.Connection.InvokeAsync(
            "Move", new MoveFigureModel { FigureId = 0, RowIndex = 5, ColumnIndex = 2 }, cancellationToken));
        Assert.Contains("It's not your turn", error.Message, StringComparison.Ordinal);

        await cats.Connection.InvokeAsync("SendChatMessage", new ChatLineSentByClientModel { GameId = gameId, Message = "  good luck  " }, cancellationToken);
        var chat = await mouse.NextAsync("ChatMessage");
        Assert.Equal("good luck", chat.GetProperty("chatLine").GetProperty("message").GetString());
        Assert.Equal((int)MessageToClientTypeEnum.ChatMessage, chat.GetProperty("typeId").GetInt32());

        await mouse.Connection.InvokeAsync("Surrender", cancellationToken);
        Assert.Equal((int)TeamEnum.Mouse, (await cats.NextAsync("PlayerHasSurrendered")).GetProperty("teamId").GetInt32());
        var finalStatus = (await cats.NextAsync("GameStatus")).GetProperty("gameStatus");
        Assert.True(finalStatus.GetProperty("players")[0].GetProperty("isWinner").GetBoolean());

        await cats.Connection.InvokeAsync("PlayerWantsToRematch", new GameIdModel { GameId = gameId }, cancellationToken);
        await mouse.NextAsync("PlayerWantsRematch");
        await mouse.Connection.InvokeAsync("PlayerWantsToRematch", new GameIdModel { GameId = gameId }, cancellationToken);
        var rematch = (await cats.NextAsync("GameStatus", status => status.GetProperty("gameStatus").GetProperty("gameId").GetString() != gameId)).GetProperty("gameStatus");
        Assert.Equal((int)TeamEnum.Mouse, rematch.GetProperty("players")[rematch.GetProperty("myPlayerIndex").GetInt32()].GetProperty("teamId").GetInt32());

        // A reconnecting player gets the game back with its feed, in order.
        await using var catsAgain = await HubClient.ConnectAsync(factory.Server, cancellationToken);
        await catsAgain.Connection.InvokeAsync("RegisterConnection", catsUserId, cancellationToken);
        Assert.Equal(rematch.GetProperty("gameId").GetString(), (await catsAgain.NextAsync("GameStatus")).GetProperty("gameStatus").GetProperty("gameId").GetString());
        Assert.True((await catsAgain.NextAsync("HasInProgressGame")).GetProperty("hasInProgressGame").GetBoolean());

        await cats.Connection.InvokeAsync("Ping", cancellationToken);
    }

    private sealed class HubClient : IAsyncDisposable
    {
        private static readonly string[] ClientMethods =
        [
            ClientMethodNames.GameList,
            ClientMethodNames.GameStart,
            ClientMethodNames.GameStatus,
            ClientMethodNames.HasInProgressGame,
            ClientMethodNames.ChatMessage,
            ClientMethodNames.PlayerHasLeftGame,
            ClientMethodNames.PlayerHasSurrendered,
            ClientMethodNames.PlayerOnlyConnectionStatusChanged,
            ClientMethodNames.PlayerWantsRematch
        ];

        private readonly Channel<(string MethodName, JsonElement Payload)> _received = Channel.CreateUnbounded<(string, JsonElement)>();

        private HubClient(HubConnection connection)
        {
            Connection = connection;
            foreach (var methodName in ClientMethods)
            {
                connection.On<JsonElement>(methodName, payload => _received.Writer.TryWrite((methodName, payload)));
            }
        }

        public HubConnection Connection { get; }

        public static async Task<HubClient> ConnectAsync(TestServer server, CancellationToken cancellationToken)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, "gameHub"), options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    options.WebSocketFactory = async (context, token) => await server.CreateWebSocketClient().ConnectAsync(context.Uri, token);
                })
                .Build();

            var client = new HubClient(connection);
            await connection.StartAsync(cancellationToken);
            return client;
        }

        /// <summary>The next message for the method that matches, skipping any before it.</summary>
        public async Task<JsonElement> NextAsync(string methodName, Func<JsonElement, bool>? predicate = null)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (true)
            {
                var (receivedMethod, payload) = await _received.Reader.ReadAsync(timeout.Token);
                if (receivedMethod == methodName && (predicate is null || predicate(payload)))
                {
                    return payload;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            return Connection.DisposeAsync();
        }
    }
}
