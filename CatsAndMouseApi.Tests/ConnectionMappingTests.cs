using CatsAndMouseGame.Hubs;

namespace CatsAndMouseApi.Tests;

public sealed class ConnectionMappingTests
{
    [Fact]
    public void Add_tracks_multiple_connections_for_same_key()
    {
        var mapping = new ConnectionMapping<string>();

        var firstCount = mapping.Add("user-1", "connection-1");
        var secondCount = mapping.Add("user-1", "connection-2");

        Assert.Equal(1, firstCount);
        Assert.Equal(2, secondCount);
        Assert.Equal(["connection-1", "connection-2"], mapping.GetConnectionsByKey("user-1"));
        Assert.Equal("user-1", mapping.GetKeyByConnection("connection-2"));
    }

    [Fact]
    public void RemoveConnection_reports_when_key_still_has_active_connections()
    {
        var mapping = new ConnectionMapping<string>();
        mapping.Add("user-1", "connection-1");
        mapping.Add("user-1", "connection-2");

        var result = mapping.RemoveConnection("connection-1");

        Assert.NotNull(result);
        Assert.Equal("user-1", result.Key);
        Assert.True(result.HasOtherActiveConnections);
        Assert.Equal(["connection-2"], mapping.GetConnectionsByKey("user-1"));
    }

    [Fact]
    public void RemoveConnection_removes_key_after_last_connection()
    {
        var mapping = new ConnectionMapping<string>();
        mapping.Add("user-1", "connection-1");

        var result = mapping.RemoveConnection("connection-1");

        Assert.NotNull(result);
        Assert.Equal("user-1", result.Key);
        Assert.False(result.HasOtherActiveConnections);
        Assert.Empty(mapping.GetConnectionsByKey("user-1"));
        Assert.Null(mapping.GetKeyByConnection("connection-1"));
    }

    [Fact]
    public void Add_moves_existing_connection_to_new_key()
    {
        var mapping = new ConnectionMapping<string>();
        mapping.Add("old-user", "connection-1");

        mapping.Add("new-user", "connection-1");

        Assert.Empty(mapping.GetConnectionsByKey("old-user"));
        Assert.Equal(["connection-1"], mapping.GetConnectionsByKey("new-user"));
        Assert.Equal("new-user", mapping.GetKeyByConnection("connection-1"));
    }

    [Fact]
    public void RemoveConnection_returns_null_for_unknown_connection()
    {
        var mapping = new ConnectionMapping<string>();

        Assert.Null(mapping.RemoveConnection("missing"));
    }
}
