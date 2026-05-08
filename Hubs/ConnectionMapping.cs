namespace CatsAndMouseApi.Hubs
{
    public sealed class ConnectionMapping<T>
        where T : notnull
    {
        private readonly Dictionary<T, HashSet<string>> _connections = [];
        private readonly Dictionary<string, T> _connectionToKey = new(StringComparer.Ordinal);
        private readonly Lock _syncRoot = new();

        public int Add(T key, string connectionId)
        {
            lock (_syncRoot)
            {
                RemoveConnectionUnderLock(connectionId);

                if (!_connections.TryGetValue(key, out var connections))
                {
                    connections = new HashSet<string>(StringComparer.Ordinal);
                    _connections.Add(key, connections);
                }

                connections.Add(connectionId);
                _connectionToKey[connectionId] = key;
                return connections.Count;
            }
        }

        public List<string> GetConnectionsByKey(T key)
        {
            lock (_syncRoot)
            {
                if (!_connections.TryGetValue(key, out var connections))
                {
                    return [];
                }

                var result = new List<string>(connections.Count);
                foreach (var connection in connections)
                {
                    result.Add(connection);
                }

                return result;
            }
        }

        public List<string> GetAllConnections()
        {
            lock (_syncRoot)
            {
                var result = new List<string>(_connectionToKey.Count);
                foreach (var connectionId in _connectionToKey.Keys)
                {
                    result.Add(connectionId);
                }

                return result;
            }
        }

        public T? GetKeyByConnection(string connectionId)
        {
            lock (_syncRoot)
            {
                return _connectionToKey.TryGetValue(connectionId, out var key) ? key : default;
            }
        }

        public RemoveConnectionResult<T>? RemoveConnection(string connectionId)
        {
            lock (_syncRoot)
            {
                return RemoveConnectionUnderLock(connectionId);
            }
        }

        private RemoveConnectionResult<T>? RemoveConnectionUnderLock(string connectionId)
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
}
