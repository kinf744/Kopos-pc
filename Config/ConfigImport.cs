using KighmuVpnWindows.Profiles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace KighmuVpnWindows.Config
{
    /// <summary>Logique d import de configuration - equivalent de ImportActivity.kt</summary>
    public class ConfigImport
    {
        private readonly string _appDataDir;

        public ConfigImport()
        {
            _appDataDir = LocalStorage.GetAppDataDir();
        }

        /// <summary>Importe une config depuis un JSON brut (.kighmu)</summary>
        public ImportResult Import(string json)
        {
            try
            {
                var obj = JObject.Parse(json);

                // Verifier le format
                if (obj["config"] == null)
                    return ImportResult.Fail("Format invalide : champ 'config' manquant");

                var configToken   = obj["config"];
                var securityToken = obj["security"];

                // Lire la securite si presente
                ConfigExport? security = null;
                if (securityToken != null)
                    security = securityToken.ToObject<ConfigExport>();

                // Verifier expiration
                if (security != null && security.ExpiresAt > 0)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (now > security.ExpiresAt)
                        return ImportResult.Fail("Cette configuration a expire");
                }

                // Verifier appId
                if (security != null && !string.IsNullOrWhiteSpace(security.AppId))
                {
                    if (security.AppId != "com.kighmu.vpn" && security.AppId != "KighmuVpnWindows")
                        return ImportResult.Fail("Config non compatible avec cette application");
                }

                // Importer les profils
                ImportProfiles(configToken);

                // Retourner le message utilisateur si present
                string userMsg = security?.UserMessage ?? "";
                bool   isBurn  = security?.BurnAfterImport ?? false;

                return ImportResult.Ok(userMsg, isBurn);
            }
            catch (Exception ex)
            {
                return ImportResult.Fail($"Erreur import: {ex.Message}");
            }
        }

        private void ImportProfiles(JToken configToken)
        {
            // SlowDNS
            TryImportList<SlowDnsProfile>(configToken, "slowDnsProfiles",
                item => new SlowDnsProfileRepository().Add(item));

            // HTTP Proxy
            TryImportList<HttpProxyProfile>(configToken, "httpProxyProfiles",
                item => new HttpProxyProfileRepository().Add(item));

            // Hysteria
            TryImportList<HysteriaProfile>(configToken, "hysteriaProfiles",
                item => new HysteriaProfileRepository().Add(item));

            // Xray VPN
            TryImportList<XrayVpnProfile>(configToken, "xrayVpnProfiles",
                item => new XrayVpnProfileRepository().Add(item));

            // Xray DNS
            TryImportList<XrayDnsProfile>(configToken, "xrayDnsProfiles",
                item => new XrayDnsProfileRepository().Add(item));

            // SSH SSL
            TryImportList<SshSslProfile>(configToken, "sshSslProfiles",
                item => new SshSslProfileRepository().Add(item));
        }

        private void TryImportList<T>(JToken config, string key, Action<T> addItem)
        {
            try
            {
                var arr = config[key] as JArray;
                if (arr == null || arr.Count == 0) return;
                var list = arr.ToObject<List<T>>();
                if (list == null) return;
                foreach (var item in list)
                    addItem(item);
            }
            catch { }
        }
    }

    public class ImportResult
    {
        public bool   Success     { get; private set; }
        public string Message     { get; private set; } = "";
        public string UserMessage { get; private set; } = "";
        public bool   IsBurn      { get; private set; }

        public static ImportResult Ok(string userMsg, bool isBurn) =>
            new ImportResult { Success = true, UserMessage = userMsg, IsBurn = isBurn, Message = "Import reussi" };

        public static ImportResult Fail(string message) =>
            new ImportResult { Success = false, Message = message };
    }
}
