using KighmuVpnWindows.Config;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>
    /// SSH SSL/TLS : configuration unique (pas de liste de profils).
    /// Stockée dans le fichier Prefs\ssh_ssl.json via LocalStorage.
    /// </summary>
    public class SshSslProfileRepository
    {
        private const string PrefsName = "ssh_ssl";
        private const string Key       = "ssh_ssl_config";

        private readonly LocalStorage _storage;

        public SshSslProfileRepository()
        {
            _storage = new LocalStorage(PrefsName);
        }

        public SshSslProfile GetConfig()
        {
            var json = _storage.GetString(Key, "");
            if (string.IsNullOrWhiteSpace(json))
                return new SshSslProfile();
            return SshSslProfile.FromJson(json);
        }

        public void SaveConfig(SshSslProfile cfg)
        {
            _storage.SetString(Key, cfg.ToJson());
        }
    }
}
