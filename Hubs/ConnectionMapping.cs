namespace CatsAndMouseApi.Hubs;

/// <summary>
/// Which connections belong to which key (a user id), both ways. Not thread-safe:
/// <see cref="Services.GameService"/> owns the only instance and touches it under its own lock,
/// so a second lock here would only add a second acquisition to every hub call.
/// </summary>
public sealed class ConnectionMapping<T>
    where T : notnull
{
    private readonly Dictionary<T, HashSet<string>> _connections = [];
    private readonly Dictionary<string, T> _connectionToKey = [with(StringComparer.Ordinal)];

    /// <summary>Maps the connection to the key, moving it off any key it had, and returns the key's connection count.</summary>
    public int Add(T key, string connectionId)
    {
        RemoveConnection(connectionId);

        if (!_connections.TryGetValue(key, out var connections))
        {
            connections = [with(StringComparer.Ordinal)];
            _connections.Add(key, connections);
        }

        connections.Add(connectionId);
        _connectionToKey[connectionId] = key;
        return connections.Count;
    }

    /// <summary>
    /// A copy of the key's connections, safe to use after the caller's lock is released. An
    /// exactly-sized array: it is handed straight to SignalR as the recipient list.
    /// </summary>
    public string[] GetConnectionsByKey(T key)
    {
        if (!_connections.TryGetValue(key, out var connections))
        {
            return [];
        }

        var result = new string[connections.Count];
        connections.CopyTo(result);
        return result;
    }

    public T? GetKeyByConnection(string connectionId)
    {
        return _connectionToKey.TryGetValue(connectionId, out var key) ? key : default;
    }

    public RemoveConnectionResult<T>? RemoveConnection(string connectionId)
    {
        if (!_connectionToKey.Remove(connectionId, out var key))
        {
            return null;
        }

        if (!_connections.TryGetValue(key, out var connections))
        {
            return null;
        }

        connections.Remove(connectionId);
        var hasOtherActiveConnections = connections.Count > 0;

        if (!hasOtherActiveConnections)
        {
            _connections.Remove(key);
        }

        return new RemoveConnectionResult<T>(key, hasOtherActiveConnections);
    }
}

public sealed record RemoveConnectionResult<T>(T Key, bool HasOtherActiveConnections);
