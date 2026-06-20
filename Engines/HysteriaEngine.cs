using KighmuVpnWindows.Models;
using KighmuVpnWindows.Utils;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Équivalent de HysteriaEngine.kt.
    /// Pilote hysteria.exe (build Windows officiel) via Process, au lieu du .so Android.
    /// Le routage TUN se fait via hev-socks5-tunnel (Wintun integre).
    /// </summary>
    public class HysteriaEngine : ITunnelEngine
    {
        private const string TAG = "HysteriaEngine";

        private readonly HysteriaConfig _config;
        private readonly int _assignedSocksPort;
        private readonly int _profileIndex;

        private volatile bool _running;
        private volatile bool _serverConnected;
        private Process? _hysteriaProcess;
        private Process? _tun2socksProcess;
        private int _socksPort;

        public HysteriaEngine(HysteriaConfig config, int assignedSocksPort = 0, int profileIndex = 0)
        {
            _config = config;
            _assignedSocksPort = assignedSocksPort;
            _profileIndex = profileIndex;
            _socksPort = assignedSocksPort > 0 ? assignedSocksPort : GetFreePort();
        }

        public static int GetFreePort()
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
            catch
            {
                return 10800; // fallback
            }
        }

        public async Task<int> Start()
        {
            _running = true;
            _serverConnected = false;

            string ip;
            try
            {
                var entry = await Dns.GetHostEntryAsync(_config.ServerAddress);
                ip = entry.AddressList.Length > 0 ? entry.AddressList[0].ToString() : _config.ServerAddress;
            }
            catch
            {
                ip = _config.ServerAddress;
            }

            string portHopping = string.IsNullOrWhiteSpace(_config.PortHopping) ? "20000-50000" : _config.PortHopping;
            string server = $"{ip}:{portHopping}";
            KighmuLogger.Info(TAG, $"Démarrage Hysteria: {server}");

            string configFile = WriteConfig(server);
            string binary = GetBinaryPath("hysteria.exe");
            if (!File.Exists(binary))
                throw new Exception("hysteria.exe introuvable dans bin/win");

            try { _hysteriaProcess?.Kill(); } catch { /* ignore */ }
            _hysteriaProcess = null;

            StartHysteriaProcess(binary, configFile);

            // Attendre connexion serveur via les logs (max 15s, comme côté Android)
            for (int i = 0; i < 30 && !_serverConnected; i++)
            {
                if (_hysteriaProcess == null || _hysteriaProcess.HasExited) break;
                await Task.Delay(500);
            }

            if (!_serverConnected)
                throw new Exception("Hysteria: connexion serveur impossible");

            KighmuLogger.Info(TAG, $"Hysteria prêt sur port {_socksPort} ✅");
            return _socksPort;
        }

        private string WriteConfig(string server)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KighmuVPN", "Configs"
            );
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"hysteria_config_{_profileIndex}.json");

            var configObj = new
            {
                server,
                obfs = _config.ObfsPassword,
                auth_str = _config.AuthPassword,
                up_mbps = _config.UploadMbps,
                down_mbps = _config.DownloadMbps,
                retry = 3,
                retry_interval = 1,
                socks5 = new { listen = $"127.0.0.1:{_socksPort}" },
                insecure = true,
                recv_window_conn = 4194304,
                recv_window = 16777216
            };

            File.WriteAllText(path, JsonConvert.SerializeObject(configObj, Formatting.Indented));
            KighmuLogger.Info(TAG, $"Config écrite: {server}");
            return path;
        }

        private static string GetBinaryPath(string name) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "win", name);

        private void StartHysteriaProcess(string binary, string configFile)
        {
            var psi = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = $"client --config \"{configFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _hysteriaProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            bool loggedConnected = false;
            _hysteriaProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null || !_running) return;
                string lineLower = e.Data.ToLowerInvariant();

                bool looksConnected = lineLower.Contains("connected") ||
                    (lineLower.Contains("socks5") && e.Data.Contains("127.0.0.1:")) ||
                    (lineLower.Contains("udp") && (lineLower.Contains("session") || lineLower.Contains("running")));

                if (looksConnected && !loggedConnected)
                {
                    _serverConnected = true;
                    loggedConnected = true;
                    var match = Regex.Match(e.Data, @"127\.0\.0\.1:(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int port) && port > 0)
                        _socksPort = port;
                    KighmuLogger.Info(TAG, "Hysteria connecté ✅");
                }
                else if (lineLower.Contains("error") || lineLower.Contains("fatal"))
                {
                    KighmuLogger.Error(TAG, $"Hysteria erreur: {e.Data}");
                }
            };

            _hysteriaProcess.Exited += (s, e) =>
            {
                KighmuLogger.Info(TAG, $"Hysteria exit: {_hysteriaProcess?.ExitCode}");
                _serverConnected = false;
            };

            _hysteriaProcess.Start();
            _hysteriaProcess.BeginOutputReadLine();
            _hysteriaProcess.BeginErrorReadLine();
            KighmuLogger.Info(TAG, "Hysteria PID démarré: " + _hysteriaProcess.Id);
        }

        /// <summary>
        /// Démarre hev-socks5-tunnel pour router le trafic de l'adaptateur Wintun
        /// vers le SOCKS5 local (127.0.0.1:_socksPort).
        /// Remplace HevTun2Socks (lib native Android) par un process séparé.
        /// </summary>
        public void StartTun2Socks(string tunAdapterName) => StartTun2SocksOnPort(tunAdapterName, _socksPort);

        /// <summary>
        /// Variante permettant de forcer un port cible précis (utilisé par
        /// MultiHysteriaEngine pour pointer vers le port du SocksBalancer
        /// plutôt que le port direct de cet engine).
        /// </summary>
        public void StartTun2SocksOnPort(string tunAdapterName, int targetPort)
        {
            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, TAG);
        }

        public async Task Stop()
        {
            _running = false;
            _serverConnected = false;
            KighmuLogger.Info(TAG, "Arrêt forcé de Hysteria et tun2socks...");

            await Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(true); } catch { /* ignore */ }
                try { _hysteriaProcess?.Kill(true); } catch { /* ignore */ }
            });

            _tun2socksProcess = null;
            _hysteriaProcess = null;
            KighmuLogger.Info(TAG, "Hysteria arrêté ✅");
        }

        public bool IsRunning() => _running && _serverConnected;
    }
}
