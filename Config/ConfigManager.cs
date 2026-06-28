using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace KighmuVpnWindows.Config
{
    /// <summary>Gestionnaire principal : import, export, reset - equivalent de ConfigManager.kt</summary>
    public class ConfigManager
    {
        private readonly string _appDataDir;

        public ConfigManager()
        {
            _appDataDir = LocalStorage.GetAppDataDir();
        }

        /// <summary>Importe une config depuis un JSON brut (.kighmu)</summary>
        public ImportResult ImportConfig(string json)
        {
            var importer = new ConfigImport();
            return importer.Import(json);
        }

        /// <summary>Exporte toute la config en JSON (.kighmu)</summary>
        public string ExportConfig()
        {
            var config = new JObject();

            // Exporter tous les profils
            config["slowDnsProfiles"]   = JArray.FromObject(new SlowDnsProfileRepository().GetAll());
            config["httpProxyProfiles"] = JArray.FromObject(new HttpProxyProfileRepository().GetAll());
            config["hysteriaProfiles"]  = JArray.FromObject(new HysteriaProfileRepository().GetAll());
            config["xrayVpnProfiles"]   = JArray.FromObject(new XrayVpnProfileRepository().GetAll());
            config["xrayDnsProfiles"]   = JArray.FromObject(new XrayDnsProfileRepository().GetAll());
            config["sshSslProfiles"]    = JArray.FromObject(new SshSslProfileRepository().GetAll());

            var security = new ConfigExport
            {
                AppId       = "KighmuVpnWindows",
                ExportType  = "normal",
                ExportedAt  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AppVersion  = "1.0"
            };

            var exportPackage = new JObject
            {
                ["config"]      = config,
                ["security"]    = JObject.FromObject(security),
                ["exportedAt"]  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["appVersion"]  = "1.0"
            };

            return exportPackage.ToString(Formatting.Indented);
        }

        /// <summary>Reinitialise toute la configuration - equivalent de resetConfig() MainActivity.kt</summary>
        public void ResetConfig()
        {
            // Supprimer tous les profils
            new SlowDnsProfileRepository().DeleteAll();
            new HttpProxyProfileRepository().DeleteAll();
            new HysteriaProfileRepository().DeleteAll();
            new XrayVpnProfileRepository().DeleteAll();
            new XrayDnsProfileRepository().DeleteAll();
            new SshSslProfileRepository().DeleteAll();

            // Supprimer les preferences
            string prefsDir = AppPaths.PrefsPath;

            if (Directory.Exists(prefsDir))
            {
                foreach (var file in Directory.GetFiles(prefsDir, "*.json"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
    }
}
