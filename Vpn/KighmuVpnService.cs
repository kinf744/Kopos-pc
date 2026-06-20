using KighmuVpnWindows.Engines;
using KighmuVpnWindows.Models;
using KighmuVpnWindows.Utils;
using System;
using System.Threading.Tasks;

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

            SetStatus(ConnectionStatus.CONNECTING);

            try
            {
                // 1. Creer l'engine selon le mode actif
                _engine = TunnelEngineFactory.Create();
                KighmuLogger.Info(TAG, $"Engine cree: {_engine.GetType().Name}");

                // 2. Demarrer l'engine -> obtenir le port SOCKS5/HTTP local
                int proxyPort = await _engine.Start();
                KighmuLogger.Info(TAG, $"Engine demarre sur port {proxyPort}");

                // 3. Demarrer tun2socks vers l'adaptateur Wintun
                _engine.StartTun2Socks(TUN_ADAPTER);
                KighmuLogger.Info(TAG, $"tun2socks demarre sur adaptateur [{TUN_ADAPTER}]");

                // 4. Configurer le routage systeme Windows (force tout le trafic -> tunnel)
                await Task.Delay(800);
                bool routesOk = RouteManager.ApplyRoutes(TUN_ADAPTER, dnsServer: "198.18.0.1");
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
                if (_engine != null)
                {
                    await _engine.Stop();
                    _engine = null;
                    KighmuLogger.Info(TAG, "Engine arrete");
                }

                RouteManager.RemoveRoutes();
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
    }
}
