using KighmuVpnWindows.Engines;
using KighmuVpnWindows.Models;
using KighmuVpnWindows.Utils;
using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace KighmuVpnWindows.Vpn
{
    /// <summary>
    /// Equivalent de KighmuVpnService.kt pour Windows.
    /// Orchestre : TunnelEngineFactory -> ITunnelEngine -> Wintun/tun2socks.
    /// Singleton accessible via KighmuVpnService.Instance.
    /// </summary>
    public class KighmuVpnService
    {
        private const string TAG             = "KighmuVpnService";
        private const string TUN_ADAPTER     = "KighmuVPN";

        // ── Singleton ────────────────────────────────────────────────────────
        private static KighmuVpnService? _instance;
        public  static KighmuVpnService  Instance => _instance ??= new KighmuVpnService();
        private KighmuVpnService() { }

        // ── Etat ─────────────────────────────────────────────────────────────
        private ITunnelEngine?   _engine;
        private DnsProxy?          _dnsProxy;
        private ConnectionStatus _status = ConnectionStatus.DISCONNECTED;

        public ConnectionStatus Status => _status;
        public bool IsConnected => _status == ConnectionStatus.CONNECTED;

        // ── Evenements UI ─────────────────────────────────────────────────────
        public event Action<ConnectionStatus>? StatusChanged;
        public event Action<string>?           ErrorOccurred;
        public event Action<TunnelMode>?        ActiveModeChanged;

        // ── API publique ──────────────────────────────────────────────────────

        /// <summary>
        /// Demarre le tunnel selon le mode actif dans TunnelEngineFactory.
        /// Equivalent de onStartCommand() / connectTunnel() cote Android.
        /// </summary>
        public async Task StartTunnel()
        {
            if (_status == ConnectionStatus.CONNECTED || _status == ConnectionStatus.CONNECTING)
            {
                KighmuLogger.Warn(TAG, "Tunnel deja actif ou en cours de connexion");
                return;
            }

            // Securite : si un arret precedent a laisse des routes residuelles, les nettoyer
            if (_status == ConnectionStatus.ERROR)
            {
                KighmuLogger.Info(TAG, "Nettoyage post-erreur avant reconnexion...");
                RouteManager.RemoveRoutes();
                await Task.Delay(500);
            }

            SetStatus(ConnectionStatus.CONNECTING);

            try
            {
                // 1. Demarrer le proxy DNS local (DNS bypass tunnel)
                try
                {
                    string? physIp = DetectPhysicalIp();
                    var dnsServers = DetectSystemDnsServers();
                    if (!string.IsNullOrEmpty(physIp) && dnsServers.Count > 0)
                    {
                        _dnsProxy = new DnsProxy(dnsServers[0], 53, physIp, 53);
                        _dnsProxy.Start();
                        KighmuLogger.Info(TAG, $"DnsProxy: 127.0.0.1:53 -> {dnsServers[0]}:53 (bind={physIp})");
                    }
                    else
                        KighmuLogger.Warn(TAG, $"DnsProxy: detect IP={physIp ?? "(null)"} DNS={dnsServers.Count}");
                }
                catch (Exception ex)
                {
                    KighmuLogger.Warn(TAG, $"DnsProxy start error: {ex.Message}");
                    try { _dnsProxy?.Dispose(); } catch { }
                    _dnsProxy = null;
                }

                // 2. Creer l'engine selon le mode actif
                _engine = TunnelEngineFactory.Create();
                KighmuLogger.Info(TAG, $"Engine cree: {_engine.GetType().Name}");

                // 2. Demarrer l'engine -> obtenir le port SOCKS5/HTTP local
                int proxyPort = await _engine.Start();
                KighmuLogger.Info(TAG, $"Engine demarre sur port {proxyPort}");

                // 3. Exclure IP serveur AVANT de demarrer tun2socks
                // Garantit que les paquets UDP (Hysteria) ne sont pas captures par le tunnel
                string? serverIp = _engine.ServerIp;
                if (!string.IsNullOrWhiteSpace(serverIp))
                {
                    RouteManager.AddServerExclusions(serverIp);
                    KighmuLogger.Info(TAG, $"Routes exclusion pre-appliquees pour: {serverIp}");
                    await Task.Delay(300);
                }

                // 4. Demarrer tun2socks vers l'adaptateur Wintun
                _engine.StartTun2Socks(TUN_ADAPTER);
                KighmuLogger.Info(TAG, $"tun2socks demarre sur adaptateur [{TUN_ADAPTER}]");

                // 5. Configurer le routage systeme Windows (force tout le trafic -> tunnel)
                // Attendre que hev-socks5-tunnel ait cree l'adaptateur Wintun (max 5s)
                await Task.Delay(1500);
                bool routesOk = RouteManager.ApplyRoutes(TUN_ADAPTER, serverIp: serverIp, dnsServer: "8.8.8.8");
                if (routesOk && _dnsProxy != null)
                {
                    int? idx = RouteManager.GetAdapterIndex(TUN_ADAPTER);
                    if (idx.HasValue)
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface ip set dns name={idx.Value} static 127.0.0.1",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(psi);
                        KighmuLogger.Info(TAG, "DNS TUN pointe vers 127.0.0.1 (DnsProxy)");
                    }
                }

                // Dump table de routage et DNS apres configuration
                try { var rp = Process.Start(new ProcessStartInfo { FileName = "route", Arguments = "print -4", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (rp != null) { string rt = rp.StandardOutput.ReadToEnd(); rp.WaitForExit(3000); SlowDnsLogger.Block(TAG, "Table de routage APRES routes", rt); } } catch { }
                try { var dp = Process.Start(new ProcessStartInfo { FileName = "netsh", Arguments = "interface ipv4 show dns", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (dp != null) { string dc = dp.StandardOutput.ReadToEnd(); dp.WaitForExit(3000); SlowDnsLogger.Block(TAG, "Config DNS Windows (apres)", dc); } } catch { }
                if (!routesOk)
                    throw new Exception("Impossible de configurer le routage systeme (verifiez les droits administrateur).");

                SetStatus(ConnectionStatus.CONNECTED);
                KighmuLogger.Info(TAG, "Tunnel actif !");
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur demarrage tunnel: {ex.Message}");
                ErrorOccurred?.Invoke(ex.Message);
                await StopTunnel();
                SetStatus(ConnectionStatus.ERROR);
            }
        }

        /// <summary>
        /// Arrete le tunnel proprement.
        /// Equivalent de stopSelf() / disconnectTunnel() cote Android.
        /// </summary>
        public async Task StopTunnel()
        {
            if (_status == ConnectionStatus.DISCONNECTED)
                return;

            SetStatus(ConnectionStatus.STOPPING);

            try
            {
                // 1. Supprimer les routes AVANT de tuer les processus
                RouteManager.RemoveRoutes();
                KighmuLogger.Info(TAG, "Routes supprimees");

                // 1b. Arreter le proxy DNS local
                if (_dnsProxy != null)
                {
                    _dnsProxy.Dispose();
                    _dnsProxy = null;
                    KighmuLogger.Info(TAG, "DnsProxy arrete");
                }

                // 2. Arreter l'engine (tue les processus et attend leur fin)
                if (_engine != null)
                {
                    await _engine.Stop();
                    _engine = null;
                    KighmuLogger.Info(TAG, "Engine arrete");
                }

                // 3. Delai de securite : laisser Windows liberer les ressources
                // (sockets TIME_WAIT, handles Wintun, etc.)
                await Task.Delay(1000);
                KighmuLogger.Info(TAG, "Ressources liberees");
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur arret tunnel: {ex.Message}");
            }
            finally
            {
                SetStatus(ConnectionStatus.DISCONNECTED);
            }
        }

        /// <summary>
        /// Bascule connexion/deconnexion (appele depuis HomeView).
        /// </summary>
        public async Task ToggleTunnel()
        {
            if (IsConnected || _status == ConnectionStatus.CONNECTING)
                await StopTunnel();
            else
                await StartTunnel();
        }

        /// <summary>
        /// Mode actif courant (lu depuis LocalStorage).
        /// </summary>
        public TunnelMode ActiveMode => TunnelEngineFactory.GetActiveMode();

        /// <summary>
        /// Change le mode actif (appele depuis ConfigView).
        /// Arrete le tunnel si actif avant de changer.
        /// </summary>
        public async Task SetMode(TunnelMode mode)
        {
            if (IsConnected)
            {
                KighmuLogger.Info(TAG, "Arret tunnel avant changement de mode...");
                await StopTunnel();
            }
            TunnelEngineFactory.SetActiveMode(mode);
            ActiveModeChanged?.Invoke(mode);
        }

        // ── Interne ───────────────────────────────────────────────────────────

        private void SetStatus(ConnectionStatus status)
        {
            _status = status;
            KighmuLogger.Info(TAG, $"Status -> {status}");
            StatusChanged?.Invoke(status);
        }

        private static string? DetectPhysicalIp()
        {
            try
            {
                var psi = new ProcessStartInfo { FileName = "route", Arguments = "print -4 0.0.0.0", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                string output = p!.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                foreach (var line in output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = line.Trim();
                    if (!t.StartsWith("0.0.0.0")) continue;
                    var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 && parts[3].Contains("."))
                        return parts[3];
                }
            }
            catch { }
            return null;
        }

        private static List<string> DetectSystemDnsServers()
        {
            var servers = new List<string>();
            try
            {
                var psi = new ProcessStartInfo { FileName = "netsh", Arguments = "interface ipv4 show dns", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                string output = p!.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                foreach (var line in output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = line.Trim();
                    if (!t.Contains("DNS") && !t.Contains("dns")) continue;
                    var parts = t.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                        if (part.Contains('.') && System.Net.IPAddress.TryParse(part, out _))
                            if (!servers.Contains(part)) servers.Add(part);
                }
            }
            catch { }
            if (servers.Count == 0) servers.AddRange(new[] { "8.8.8.8", "8.8.4.4", "1.1.1.1" });
            return servers;
        }
    }
}
