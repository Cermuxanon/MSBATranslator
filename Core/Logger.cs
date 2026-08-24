namespace MSBATranslator.Core
{
    public static class Logger
    {
        public static readonly List<string> Logs = new();
        public static readonly object SyncRoot = new();
        private const int MaxLogCount = 2000;

        private static int _version = 0;
        private static int _cachedVersion = -1;
        private static string[] _cachedSnapshot = Array.Empty<string>();

        public static void Log(string message)
        {
            string entry = $"{DateTime.Now:HH:mm:ss} | {message}";
            lock (SyncRoot)
            {
                Logs.Add(entry);
                if (Logs.Count > MaxLogCount)
                {
                    Logs.RemoveAt(0);
                }
                _version++;
            }
            Console.WriteLine(entry);
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Logs.Clear();
                _version++;
            }
        }
        
        public static string[] GetLogsSnapshot()
        {
            lock (SyncRoot)
            {
                if (_cachedVersion != _version)
                {
                    _cachedSnapshot = Logs.ToArray();
                    _cachedVersion = _version;
                }
                return _cachedSnapshot;
            }
        }
    }
}