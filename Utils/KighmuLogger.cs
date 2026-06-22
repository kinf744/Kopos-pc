using System;
using System.IO;
using System.Text.RegularExpressions;

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
            // Effacer le log au démarrage : les logs n'ont de sens que pour la session courante
            try { if (File.Exists(LogFile)) File.WriteAllText(LogFile, string.Empty); }
            catch { /* best effort */ }
        }

        /// <summary>Retourne les N dernieres lignes du fichier de log (pour affichage historique).</summary>
        public static string[] GetRecentLines(int maxLines = 200)
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(LogFile)) return Array.Empty<string>();
                    var allLines = File.ReadAllLines(LogFile);
                    return allLines.Length <= maxLines ? allLines : GetLast(allLines, maxLines);
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }
        }

        private static string[] GetLast(string[] arr, int count)
        {
            var result = new string[count];
            Array.Copy(arr, arr.Length - count, result, 0, count);
            return result;
        }

        public static void Info(string tag, string message) => Write("INFO", tag, message);
        public static void Error(string tag, string message) => Write("ERROR", tag, message);
        public static void Warning(string tag, string message) => Write("WARN", tag, message);
        public static void Warn(string tag, string message) => Write("WARN", tag, message); // alias

        /// <summary>Supprime les sequences ANSI (couleurs, curseur) pour affichage propre dans WPF TextBlock.</summary>
        private static string StripAnsi(string text) =>
            Regex.Replace(text ?? "", @"\x1B\[[0-9;]*[mKHFABCDJsu]|\x1B\([A-Z]|\x1B[=>]|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

        private static void Write(string level, string tag, string message)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string clean = StripAnsi(message);
            string line = $"[{ts}] [{level}] [{tag}] {clean}";

            lock (_lock)
            {
                try { File.AppendAllText(LogFile, line + Environment.NewLine); }
                catch { /* best effort, ne jamais planter sur erreur de log */ }
            }

            OnLogLine?.Invoke(line);
        }
    }
}
