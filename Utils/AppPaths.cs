using System;
using System.IO;

namespace KighmuVpnWindows.Utils
{
    public static class AppPaths
    {
        private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static readonly string BasePath  = Path.Combine(LocalAppData, "KingOMVPN");
        public static readonly string BinPath   = Path.Combine(BasePath, "bin");
        public static readonly string ConfigPath = Path.Combine(BasePath, "config");
        public static readonly string LogsPath  = Path.Combine(BasePath, "logs");
        public static readonly string DataPath  = Path.Combine(BasePath, "data");
        public static readonly string PrefsPath = Path.Combine(DataPath, "prefs");
        public static readonly string CachePath = Path.Combine(BasePath, "cache");

        private static bool _directoriesCreated;

        public static void EnsureDirectories()
        {
            if (_directoriesCreated) return;
            Directory.CreateDirectory(BinPath);
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(LogsPath);
            Directory.CreateDirectory(DataPath);
            Directory.CreateDirectory(PrefsPath);
            Directory.CreateDirectory(CachePath);
            _directoriesCreated = true;
        }

        public static string Bin(string name) => Path.Combine(BinPath, name);

        public const string RESOURCE_PREFIX = "KighmuVpnWindows.bin.win.";
    }
}
