using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace KighmuVpnWindows.Config
{
    /// <summary>
    /// Équivalent de SharedPreferences Android : stockage clé-valeur persistant.
    /// Un fichier JSON par "groupe" (ex: "hysteria_profiles" -> %APPDATA%\KighmuVPN\Prefs\hysteria_profiles.json)
    /// </summary>
    public class LocalStorage
    {
        private readonly string _filePath;
        private Dictionary<string, string> _data;

        public LocalStorage(string prefsName)
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KighmuVPN", "Prefs"
            );
            Directory.CreateDirectory(baseDir);
            _filePath = Path.Combine(baseDir, $"{prefsName}.json");
            _data = Load();
        }

        private Dictionary<string, string> Load()
        {
            if (!File.Exists(_filePath)) return new Dictionary<string, string>();
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private void Persist()
        {
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(_data));
        }

        public static string GetAppDataDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KighmuVPN", "Data");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public string GetString(string key, string defaultValue) =>
            _data.TryGetValue(key, out var value) ? value : defaultValue;

        public void SetString(string key, string value)
        {
            _data[key] = value;
            Persist();
        }
    }
}
