using KighmuVpnWindows.Config;
using KighmuVpnWindows.Models;
using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using System;
using System.Linq;

namespace KighmuVpnWindows.Engines
{
    public static class TunnelEngineFactory
    {
        private const string TAG        = "TunnelEngineFactory";
        private const string PREFS_NAME = "tunnel_prefs";
        private const string KEY_MODE   = "active_tunnel_mode";

        public static TunnelMode GetActiveMode()
        {
            var prefs = new LocalStorage(PREFS_NAME);
            int id    = int.TryParse(prefs.GetString(KEY_MODE, "1"), out var v) ? v : 1;
            return TunnelModeExtensions.FromId(id);
        }

        public static void SetActiveMode(TunnelMode mode)
        {
            var prefs = new LocalStorage(PREFS_NAME);
            prefs.SetString(KEY_MODE, ((int)mode).ToString());
            KighmuLogger.Info(TAG, $"Mode actif -> {mode.Label()}");
        }

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

        // ── SlowDNS ──────────────────────────────────────────────────────────
        private static ITunnelEngine CreateSlowDns()
        {
            var repo     = new SlowDnsProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil SlowDNS configure.");

            KighmuLogger.Info(TAG, $"SlowDNS: {profiles.Count} profil(s)");

            var p0 = profiles[0];
            var cfg = new SlowDnsConfig {
                DnsServer  = p0.DnsServer,
                Nameserver = p0.Nameserver,
                PublicKey  = p0.PublicKey
            };

            if (profiles.Count == 1)
                return new SlowDnsEngine(cfg, p0.SshUser, p0.SshPass, 0);

            return new MultiSlowDnsEngine(cfg, p0.SshUser, p0.SshPass);
        }

        // ── HTTP Proxy ───────────────────────────────────────────────────────
        private static ITunnelEngine CreateHttpProxy()
        {
            var repo     = new HttpProxyProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil HTTP Proxy configure.");

            KighmuLogger.Info(TAG, $"HttpProxy: {profiles.Count} profil(s)");

            var p0 = profiles[0];
            if (profiles.Count == 1)
                return new HttpProxyEngine(
                    proxyHost    : p0.ProxyHost,
                    proxyPort    : p0.ProxyPort,
                    customPayload: p0.CustomPayload,
                    sshHost      : p0.SshHost,
                    sshPort      : p0.SshPort,
                    sshUser      : p0.SshUser,
                    sshPass      : p0.SshPass,
                    profileIndex : 0);

            return new MultiHttpProxyEngine(profiles);
        }

        // ── SSH SSL/TLS ──────────────────────────────────────────────────────
        private static ITunnelEngine CreateSshSsl()
        {
            var repo    = new SlowDnsProfileRepository();
            var profile = repo.GetSelected().FirstOrDefault()
                       ?? repo.GetAll().FirstOrDefault()
                       ?? throw new InvalidOperationException("Aucun profil SSH SSL/TLS configure.");

            KighmuLogger.Info(TAG, $"SshSsl: {profile.ProfileName}");
            return new SshSslEngine(new SshSslConfig {
                SshHost = profile.SshHost,
                SshPort = profile.SshPort,
                SshUser = profile.SshUser,
                SshPass = profile.SshPass,
                Sni     = profile.ProxyHost
            });
        }

        // ── V2Ray/Xray ───────────────────────────────────────────────────────
        private static ITunnelEngine CreateXrayVpn()
        {
            var repo    = new XrayVpnProfileRepository();
            var profile = repo.GetSelected().FirstOrDefault()
                       ?? repo.GetAll().FirstOrDefault()
                       ?? throw new InvalidOperationException("Aucun profil V2Ray/Xray configure.");

            KighmuLogger.Info(TAG, $"XrayVpn: {profile.ProfileName}");
            return new XrayVpnEngine(profile, instanceId: 0);
        }

        // ── V2Ray + SlowDNS ──────────────────────────────────────────────────
        private static ITunnelEngine CreateXrayDns()
        {
            var repo     = new XrayDnsProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil V2Ray+SlowDNS configure.");

            KighmuLogger.Info(TAG, $"XrayDns: {profiles.Count} profil(s)");

            if (profiles.Count == 1)
                return new XrayDnsEngine(profiles[0]);

            // MultiXrayDnsEngine charge ses profils lui-meme via XrayDnsProfileRepository
            return new MultiXrayDnsEngine();
        }

        // ── Hysteria UDP ─────────────────────────────────────────────────────
        private static ITunnelEngine CreateHysteria()
        {
            var repo     = new HysteriaProfileRepository();
            var selected = repo.GetSelected();
            var profiles = selected.Any() ? selected : repo.GetAll();

            if (!profiles.Any())
                throw new InvalidOperationException("Aucun profil Hysteria UDP configure.");

            KighmuLogger.Info(TAG, $"Hysteria: {profiles.Count} profil(s)");

            var p0 = profiles[0];
            if (profiles.Count == 1)
                return new HysteriaEngine(new HysteriaConfig {
                    ServerAddress = p0.ServerAddress,
                    ServerPort    = p0.ServerPort,
                    AuthPassword  = p0.AuthPassword,
                    Sni           = p0.Sni,
                    Obfs          = p0.Obfs,
                    ObfsPassword  = p0.ObfsPassword
                });

            // MultiHysteriaEngine charge ses profils lui-meme, on passe la config du 1er comme fallback
            return new MultiHysteriaEngine(new HysteriaConfig {
                ServerAddress = p0.ServerAddress,
                ServerPort    = p0.ServerPort,
                AuthPassword  = p0.AuthPassword,
                Sni           = p0.Sni,
                Obfs          = p0.Obfs,
                ObfsPassword  = p0.ObfsPassword
            });
        }
    }
}
