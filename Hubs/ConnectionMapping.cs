namespace CatsAndMouseGame.Hubs
{
    public class ConnectionMapping<T>
    {
        private readonly Dictionary<T, HashSet<string>> _connections =
            new Dictionary<T, HashSet<string>>();

        public void Add(T key, string connectionId)
        {
            lock (_connections)
            {
                HashSet<string> connections;
                if (!_connections.TryGetValue(key, out connections))
                {
                    connections = new HashSet<string>();
                    _connections.Add(key, connections);
                }

                lock (connections)
                {
                    connections.Add(connectionId);
                }
            }
        }

        public List<string> GetConnectionsByKey(T key)
        {
            HashSet<string> connections;
            if (_connections.TryGetValue(key, out connections))
            {
                return connections.ToList();
            }
            
            return new List<string>();
        }

        public List<string> GetAllConnections()
        {

            var result = new List<string>();

            HashSet<string> keyConnections;

            foreach (var key in _connections.Keys)
            {

                if (_connections.TryGetValue(key, out keyConnections))
                {
                    foreach (var keyConnection in keyConnections)
                    {
                        result.Add(keyConnection);
                    }
                }
            }
            return result;
        }

        public T GetKeyByConnection(string value)
        {

            var connection = _connections.Where(c => c.Value.Any(v => v == value)).FirstOrDefault();

            if (connection.Equals(default))
            {

                return default(T);
            }
            else
            {
                return connection.Key;
            }
        }

        public RemoveConnectionResult<T> RemoveConnection(string connectionId)
        {
            lock (_connections)
            {
                var connection = _connections.FirstOrDefault(k => k.Value.Any(c => c.Equals(connectionId)));

                if (connection.Key == null)
                {
                    return null;
                }

                connection.Value.Remove(connectionId);

                if (!connection.Value.Any())
                {
                    _connections.Remove(connection.Key);

                    return new RemoveConnectionResult<T>
                    {
                        Key = connection.Key,
                        HasOtherActiveConnections = false
                    };
                }

                return new RemoveConnectionResult<T>
                {
                    Key = connection.Key,
                    HasOtherActiveConnections = true
                };
            }
        }
    }

    public class RemoveConnectionResult<T>
    {
        public T Key { get; set; }
        public bool HasOtherActiveConnections { get; set; }
    }
}