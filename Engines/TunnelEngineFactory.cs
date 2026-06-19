using KighmuVpnWindows.Config;
using KighmuVpnWindows.Engines;
using KighmuVpnWindows.Models;
using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using System;
using System.Linq;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Equivalent de TunnelEngineFactory.kt.
    /// Lit le mode actif depuis LocalStorage et instancie le bon ITunnelEngine.
    /// </summary>
    public static class TunnelEngineFactory
    {
        private const string TAG = "TunnelEngineFactory";
        private const string PREFS_NAME = "tunnel_prefs";
        private const string KEY_MODE   = "active_tunnel_mode";

        // ── Lecture / Ecriture du mode actif ────────────────────────────────

        public static TunnelMode GetActiveMode()
        {
            var prefs = new LocalStorage(PREFS_NAME);
            int id    = int.TryParse(prefs.GetString(KEY_MODE, "1"), out var v) ? v : 1;
            return TunnelMode.FromId(id);
        }

        public static void SetActiveMode(TunnelMode mode)
        {
            var prefs = new LocalStorage(PREFS_NAME);
            prefs.SetString(KEY_MODE, ((int)mode).ToString());
            KighmuLogger.Info(TAG, $"Mode actif -> {mode.Label()}");
        }

        // ── Factory principale ───────────────────────────────────────────────

        /// <summary>
        /// Cree l'engine correspondant au mode actif.
        /// Equivalent exact de TunnelEngineFactory.kt::create().
        /// </summary>
        public static ITunnelEngine Create()
        {
            var mode = GetActiveMode();
            KighmuLogger.Info(TAG, $"Creation engine pour mode: {mode.Label()}");

            return mode switch
            {
                TunnelMode.SLOW_DNS      => CreateSlowDns(),
                TunnelMode.HTTP_PROXY    => CreateHttpProxy(),
                TunnelMode.SSH_SSL_TLS   => CreateSshSsl(),
                TunnelMode.V2RAY_XRAY    => CreateXrayVpn(),
                TunnelMode.V2RAY_SLOWDNS => CreateXrayDns(),
                TunnelMode.HYSTERIA_UDP  => CreateHysteria(),
                _ => throw new NotSupportedException($"Mode non supporte: {mode}")
            };
        }

        // ── Createurs par mode ───────────────────────────────────────────────

        private static ITunnelEngine CreateSlowDns()
        {
            var repo     = new SlowDnsProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil SlowDNS configure.");

            KighmuLogger.Info(TAG, $"SlowDNS: {profiles.Count} profil(s)");

            return profiles.Count == 1
                ? (ITunnelEngine)new SlowDnsEngine(profiles[0])
                : new MultiSlowDnsEngine(profiles);
        }

        private static ITunnelEngine CreateHttpProxy()
        {
            var repo     = new HttpProxyProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil HTTP Proxy configure.");

            KighmuLogger.Info(TAG, $"HttpProxy: {profiles.Count} profil(s)");

            return profiles.Count == 1
                ? (ITunnelEngine)new HttpProxyEngine(profiles[0])
                : new MultiHttpProxyEngine(profiles);
        }

        private static ITunnelEngine CreateSshSsl()
        {
            var repo     = new SlowDnsProfileRepository();   // SSH SSL reutilise SlowDnsProfile (champs SSH)
            var selected = repo.GetSelected();
            var profile  = selected.FirstOrDefault()
                        ?? repo.GetAll().FirstOrDefault()
                        ?? throw new InvalidOperationException("Aucun profil SSH SSL/TLS configure.");

            KighmuLogger.Info(TAG, $"SshSsl: {profile.ProfileName}");
            return new SshSslEngine(profile);
        }

        private static ITunnelEngine CreateXrayVpn()
        {
            var repo     = new XrayVpnProfileRepository();
            var selected = repo.GetSelected();
            var profile  = selected.FirstOrDefault()
                        ?? repo.GetAll().FirstOrDefault()
                        ?? throw new InvalidOperationException("Aucun profil V2Ray/Xray configure.");

            KighmuLogger.Info(TAG, $"XrayVpn: {profile.ProfileName}");
            return new XrayVpnEngine(profile, instanceId: 0);
        }

        private static ITunnelEngine CreateXrayDns()
        {
            var repo     = new XrayDnsProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil V2Ray+SlowDNS configure.");

            KighmuLogger.Info(TAG, $"XrayDns: {profiles.Count} profil(s)");

            return profiles.Count == 1
                ? (ITunnelEngine)new XrayDnsEngine(profiles[0])
                : new MultiXrayDnsEngine(profiles);
        }

        private static ITunnelEngine CreateHysteria()
        {
            var repo     = new HysteriaProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil Hysteria UDP configure.");

            KighmuLogger.Info(TAG, $"Hysteria: {profiles.Count} profil(s)");

            return profiles.Count == 1
                ? (ITunnelEngine)new HysteriaEngine(profiles[0])
                : new MultiHysteriaEngine(profiles);
        }
    }
}
