using System.Reflection;
using System.Text;
using System.Text.Json;
using CatsAndMouseApi.Enums;
using CatsAndMouseApi.Hubs;
using CatsAndMouseApi.Models;
using CatsAndMouseApi.Models.MessagesToClient;
using CatsAndMouseApi.Serialization;

namespace CatsAndMouseApi.Tests;

/// <summary>
/// The published API is Native AOT, where JSON metadata exists only for the types listed in
/// <see cref="GameJsonSerializerContext"/>. These tests fail on the JIT, long before a deploy,
/// when a hub method or message type is added without it.
/// </summary>
public sealed class SerializationContractTests
{
    private static readonly JsonSerializerOptions HubOptions = CreateHubOptions();

    public static TheoryData<Type> HubPayloadTypes()
    {
        var types = new HashSet<Type>();

        foreach (var method in typeof(GameHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in method.GetParameters())
            {
                types.Add(parameter.ParameterType);
            }

            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                types.Add(method.ReturnType.GetGenericArguments()[0]);
            }
        }

        foreach (var type in typeof(IMessageToClient).Assembly.GetTypes())
        {
            if (type.IsClass && !type.IsAbstract && typeof(IMessageToClient).IsAssignableFrom(type))
            {
                types.Add(type);
            }
        }

        return [.. types.Where(type => type != typeof(Exception))];
    }

    [Theory]
    [MemberData(nameof(HubPayloadTypes))]
    public void Every_hub_payload_type_has_generated_metadata(Type type)
    {
        Assert.NotNull(GameJsonSerializerContext.Default.GetTypeInfo(type));
    }

    [Fact]
    public void The_hub_options_never_fall_back_to_reflection()
    {
        // With the context in the resolver chain the serializer has no reflection fallback, on
        // the JIT just as under Native AOT, so a missing type fails here rather than in production.
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(new UnlistedType(), HubOptions));
    }

    [Fact]
    public void Game_status_has_the_wire_shape_the_ui_reads_and_no_user_ids()
    {
        var game = new GameModel("abcde", null, TimeProvider.System);
        game.SetFirstPlayer(TeamEnum.Cats, "Cats", "cats-secret-user-id");
        game.SetSecondPlayer("Mouse", "mouse-secret-user-id");
        game.Start();
        IMessageToClient message = new GameStatusMessage(new GameStatusForPlayerModel(game.Id, game.CreatePlayerSnapshots(), 1));

        var json = JsonSerializer.Serialize(message, message.GetType(), HubOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal((int)MessageToClientTypeEnum.GameStatus, root.GetProperty("typeId").GetInt32());
        var status = root.GetProperty("gameStatus");
        Assert.Equal("abcde", status.GetProperty("gameId").GetString());
        Assert.Equal(1, status.GetProperty("myPlayerIndex").GetInt32());

        var mouse = status.GetProperty("players")[1];
        Assert.Equal("Mouse", mouse.GetProperty("name").GetString());
        Assert.True(mouse.GetProperty("isTheirTurn").GetBoolean());
        Assert.Equal((int)TeamEnum.Mouse, mouse.GetProperty("teamId").GetInt32());
        Assert.False(mouse.GetProperty("isWinner").GetBoolean());
        Assert.False(mouse.GetProperty("hasUserLeftTheGame").GetBoolean());
        Assert.False(mouse.GetProperty("wantsToRematch").GetBoolean());

        var figure = mouse.GetProperty("figures")[0];
        Assert.Equal(0, figure.GetProperty("id").GetInt32());
        Assert.Equal((int)FigureTypeEnum.Mouse, figure.GetProperty("typeId").GetInt32());
        Assert.Equal(7, figure.GetProperty("position").GetProperty("rowIndex").GetInt32());
        Assert.Equal(4, figure.GetProperty("position").GetProperty("columnIndex").GetInt32());
        Assert.Equal(2, figure.GetProperty("canMoveToPositions").GetArrayLength());

        Assert.DoesNotContain("secret-user-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("userId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Feed_messages_serialize_as_the_case_itself_with_their_type_id()
    {
        GameFeedMessage entry = new PlayerOnlyConnectionStatusChangedMessage("abcde", "Cats", TeamEnum.Cats, IsConnected: false);

        var json = JsonSerializer.Serialize(entry.Message, entry.Message.GetType(), HubOptions);

        Assert.Equal(ClientMethodNames.PlayerOnlyConnectionStatusChanged, entry.ClientMethodName);
        Assert.Equal(
            """{"gameId":"abcde","userName":"Cats","teamId":1,"isConnected":false,"typeId":9}""",
            json);
    }

    [Fact]
    public void Hub_arguments_bind_from_the_ui_camel_case_json()
    {
        var model = JsonSerializer.Deserialize<CancelGameModel>(
            Encoding.UTF8.GetBytes("""{"gameId":"abcde","userId":"user-1"}"""),
            HubOptions)!;

        Assert.Equal("abcde", model.GameId);
        Assert.Equal("user-1", model.UserId);
        Assert.True(model.SendAwaitingGamesToAllClients);
    }

    private static JsonSerializerOptions CreateHubOptions()
    {
        // The same settings the SignalR JSON protocol starts from, plus the context Program adds.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.TypeInfoResolverChain.Insert(0, GameJsonSerializerContext.Default);
        options.MakeReadOnly();
        return options;
    }

    private sealed class UnlistedType
    {
        public int Value { get; init; }
    }
}
