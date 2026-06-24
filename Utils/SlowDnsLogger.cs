using System;
using System.IO;
using System.Text;

namespace KighmuVpnWindows.Utils
{
    /// <summary>
    /// Logger dedie au tunnel SlowDNS.
    /// Ecrit sur le Bureau dans kighmu_slow.log avec le maximum de details.
    /// </summary>
    public static class SlowDnsLogger
    {
        private static readonly string LogPath;
        private static readonly object _lock = new();

        static SlowDnsLogger()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                LogPath = Path.Combine(desktop, "kighmu_slow.log");
                File.WriteAllText(LogPath, $"=== KighmuVPN SlowDNS Debug Log ===\r\n" +
                    $"Demarrage: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                    $"Machine: {Environment.MachineName}, User: {Environment.UserName}\r\n" +
                    $"OS: {Environment.OSVersion}\r\n" +
                    $"=== ===\r\n\r\n");
            }
            catch { LogPath = ""; }
        }

        public static bool IsEnabled => !string.IsNullOrEmpty(LogPath);

        public static void Info(string tag, string message) => Write("INFO", tag, message);
        public static void Error(string tag, string message) => Write("ERROR", tag, message);
        public static void Warn(string tag, string message) => Write("WARN", tag, message);

        /// <summary>Log brut sans formatage (pour stdout/stderr des processus).</summary>
        public static void Raw(string tag, string data)
        {
            if (!IsEnabled || string.IsNullOrEmpty(data)) return;
            lock (_lock)
            {
                try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {data}\r\n"); }
                catch { }
            }
        }

        /// <summary>Log un bloc de texte multi-lignes (ex: configuration, routage).</summary>
        public static void Block(string tag, string title, string content)
        {
            if (!IsEnabled) return;
            Write("BLOCK", tag, $">>> {title} <<<");
            foreach (var line in content.Split(new[] { '\r\n', '\n', '\r' }, StringSplitOptions.None))
            {
                if (!string.IsNullOrEmpty(line))
                    Raw(tag, "  | " + line);
            }
            Write("BLOCK", tag, $"<<< fin {title} >>>");
        }

        /// <summary>Log du debut d'une operation.</summary>
        public static void Begin(string tag, string operation)
        {
            Write("BEGIN", tag, $"--- {operation} ---");
        }

        /// <summary>Log de la fin d'une operation avec duree.</summary>
        public static void End(string tag, string operation, bool success, long elapsedMs = 0)
        {
            string status = success ? "OK" : "ECHEC";
            string dur = elapsedMs > 0 ? $" ({elapsedMs}ms)" : "";
            Write("END", tag, $"--- {operation}: {status}{dur} ---");
        }

        /// <summary>Log un dump hexa d'un buffer (max 64 octets).</summary>
        public static void Hex(string tag, string label, byte[] data, int maxLen = 64)
        {
            if (!IsEnabled || data == null || data.Length == 0) return;
            int len = Math.Min(data.Length, maxLen);
            var sb = new StringBuilder();
            sb.Append($"{label} ({data.Length} bytes): ");
            for (int i = 0; i < len; i++)
                sb.Append($"{data[i]:X2}");
            if (data.Length > maxLen) sb.Append("...");
            Raw(tag, sb.ToString());
        }

        private static void Write(string level, string tag, string message)
        {
            if (!IsEnabled) return;
            lock (_lock)
            {
                try
                {
                    string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.AppendAllText(LogPath, $"[{ts}] [{level}] [{tag}] {message}\r\n");
                }
                catch { }
            }
        }
    }
}
