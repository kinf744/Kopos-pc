using System;
using System.IO;

namespace KighmuVpnWindows.Utils
{
    /// <summary>Équivalent de KighmuLogger.kt côté Android.</summary>
    public static class KighmuLogger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KighmuVPN", "Logs"
        );
        private static readonly string LogFile = Path.Combine(LogDir, "kighmu.log");
        private static readonly object _lock = new object();

        public static event Action<string>? OnLogLine;

        static KighmuLogger()
        {
            Directory.CreateDirectory(LogDir);
        }

        public static void Info(string tag, string message) => Write("INFO", tag, message);
        public static void Error(string tag, string message) => Write("ERROR", tag, message);
        public static void Warning(string tag, string message) => Write("WARN", tag, message);

        private static void Write(string level, string tag, string message)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string line = $"[{ts}] [{level}] [{tag}] {message}";

            lock (_lock)
            {
                try { File.AppendAllText(LogFile, line + Environment.NewLine); }
                catch { /* best effort, ne jamais planter sur erreur de log */ }
            }

            OnLogLine?.Invoke(line);
        }
    }
}
