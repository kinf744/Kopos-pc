using KighmuVpnWindows.Config;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>
    /// SSH SSL/TLS : configuration unique (pas de liste de profils).
    /// Les methodes GetAll/Add/DeleteAll/GetActive sont fournies pour
    /// compatibilite avec ConfigManager, ConfigImport et TunnelEngineFactory.
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

        // ── Config unique (utilisé par ConfigView) ───────────────────────────
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

        // ── Compatibilité ConfigManager / ConfigImport ───────────────────────
        /// <summary>Retourne la config unique dans une liste (pour export).</summary>
        public List<SshSslProfile> GetAll()
        {
            return new List<SshSslProfile> { GetConfig() };
        }

        /// <summary>Utilisé par import : remplace la config unique.</summary>
        public void Add(SshSslProfile profile)
        {
            SaveConfig(profile);
        }

        /// <summary>Réinitialise la config unique.</summary>
        public void DeleteAll()
        {
            SaveConfig(new SshSslProfile());
        }

        // ── Compatibilité TunnelEngineFactory ────────────────────────────────
        /// <summary>Retourne la config unique comme profil actif.</summary>
        public SshSslProfile? GetActive()
        {
            var cfg = GetConfig();
            if (string.IsNullOrWhiteSpace(cfg.SshHost))
                return null;
            return cfg;
        }
    }
}
