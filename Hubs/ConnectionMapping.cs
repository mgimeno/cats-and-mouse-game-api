namespace CatsAndMouseGame.Hubs
{
    public sealed class ConnectionMapping<T>
        where T : notnull
    {
        private readonly Dictionary<T, HashSet<string>> _connections = new();
        private readonly Dictionary<string, T> _connectionToKey = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();

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
                return _connections.TryGetValue(key, out var connections)
                    ? connections.ToList()
                    : new List<string>();
            }
        }

        public List<string> GetAllConnections()
        {
            lock (_syncRoot)
            {
                return _connections.Values.SelectMany(c => c).Distinct(StringComparer.Ordinal).ToList();
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
