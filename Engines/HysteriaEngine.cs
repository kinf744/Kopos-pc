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
        private string _hysteriaCaptured = "";
        private readonly object _captureLock = new();
        private volatile bool _serverConnected;
        private string? _resolvedServerIp;

        public string? ServerIp => _resolvedServerIp;
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
                var listener = new TcpListener(IPAddress.Loopback, 0);
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
            SlowDnsLogger.Begin("HysteriaEngine", "START Hysteria tunnel");
            SlowDnsLogger.Info("HysteriaEngine", "=== DEBUT HYSTERIA ===");
            try { var p = Process.Start(new ProcessStartInfo { FileName = "route", Arguments = "print -4", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (p != null) { string r = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); SlowDnsLogger.Block("HysteriaEngine", "Table routage AVANT", r); } } catch { }
            try { var p = Process.Start(new ProcessStartInfo { FileName = "netsh", Arguments = "interface ipv4 show dns", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (p != null) { string r = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); SlowDnsLogger.Block("HysteriaEngine", "DNS AVANT", r); } } catch { }
            try { var p = Process.Start(new ProcessStartInfo { FileName = "netsh", Arguments = "interface ipv4 show interfaces", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (p != null) { string r = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); SlowDnsLogger.Block("HysteriaEngine", "Interfaces AVANT", r); } } catch { }
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
            _resolvedServerIp = ip;

            string portHopping = string.IsNullOrWhiteSpace(_config.PortHopping) ? "20000-50000" : _config.PortHopping;
            string server = $"{ip}:{portHopping}";
            KighmuLogger.Info(TAG, $"Demarrage Hysteria: {server}");
            SlowDnsLogger.Info("HysteriaEngine", "Serveur cible: " + server + " socksPort=" + _socksPort + " auth=" + (_config.AuthPassword ?? "(none)") + " obfs=" + (_config.ObfsPassword ?? "(none)") + " up=" + _config.UploadMbps + " down=" + _config.DownloadMbps);

            string configFile = WriteConfig(server);
            string binary = GetBinaryPath("hysteria.exe");
            if (!File.Exists(binary))
                throw new Exception("hysteria.exe introuvable dans bin/win");
            SlowDnsLogger.Info("HysteriaEngine", "Binaire: " + binary);
            try { var vi = Process.Start(new ProcessStartInfo { FileName = binary, Arguments = "version", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (vi != null) { string vout = vi.StandardOutput.ReadToEnd(); vi.WaitForExit(2000); SlowDnsLogger.Block("HysteriaEngine", "Version hysteria", vout); } } catch (Exception exv) { SlowDnsLogger.Warn("HysteriaEngine", "Version check: " + exv.Message); }

            try { _hysteriaProcess?.Kill(); } catch { /* ignore */ }
            _hysteriaProcess = null;

            KighmuLogger.Info(TAG, $"Lancement hysteria.exe: {binary} client --config {configFile}");
            SlowDnsLogger.Info("HysteriaEngine", "Lancement: " + binary + " client --config " + configFile);
            StartHysteriaProcess(binary, configFile);

            // Attendre connexion serveur via les logs (max 15s)
            KighmuLogger.Info(TAG, $"Attente connexion Hysteria (max 15s) port={_socksPort}...");
            for (int i = 0; i < 30 && !_serverConnected; i++)
            {
                if (_hysteriaProcess == null || _hysteriaProcess.HasExited)
                {
                    KighmuLogger.Error(TAG, $"Hysteria process mort prematurement a {i*500}ms exit={_hysteriaProcess?.ExitCode}");
                    break;
                }
                if (i % 4 == 0) KighmuLogger.Info(TAG, $"Attente Hysteria... {i*500}ms serverConnected={_serverConnected} socksPort={_socksPort}");
                        SlowDnsLogger.Info("HysteriaEngine", "Attente " + (i*500) + "ms sc=" + _serverConnected + " sp=" + _socksPort);
                await Task.Delay(500);
            }

            SlowDnsLogger.Info("HysteriaEngine", "Fin attente: sc=" + _serverConnected + " sp=" + _socksPort + " a=" + (_hysteriaProcess != null && !_hysteriaProcess.HasExited));
            try { SlowDnsLogger.Block("HysteriaEngine", "hysteria output complet", _hysteriaCaptured); } catch { }

            if (!_serverConnected)
                throw new Exception($"Hysteria: connexion serveur impossible (port={_socksPort})");

            SlowDnsLogger.Info("HysteriaEngine", "Hysteria SOCKS5 ready port=" + _socksPort);
            try { using var sk = new System.Net.Sockets.TcpClient(); var ct = sk.ConnectAsync(System.Net.IPAddress.Loopback, _socksPort); if (System.Threading.Tasks.Task.WhenAny(ct, System.Threading.Tasks.Task.Delay(2000)).GetAwaiter().GetResult() == ct && sk.Connected) { SlowDnsLogger.Info("HysteriaEngine", "SOCKS5 test: port=" + _socksPort + " OK"); var stream = sk.GetStream(); stream.Write(new byte[] { 5, 1, 0 }, 0, 3); byte[] buf = new byte[2]; int n = stream.Read(buf, 0, 2); SlowDnsLogger.Info("HysteriaEngine", "SOCKS5 handshake: auth=" + (n == 2 ? buf[1].ToString() : "fail")); } else SlowDnsLogger.Warn("HysteriaEngine", "SOCKS5 test: INACCESSIBLE"); } catch (Exception ex) { SlowDnsLogger.Warn("HysteriaEngine", "SOCKS5 test error: " + ex.Message); }
            SlowDnsLogger.Info("HysteriaEngine", "SOCKS5 test TCP+UDP port=" + _socksPort);
            try { using var sk = new TcpClient(); var ct = sk.ConnectAsync(IPAddress.Loopback, _socksPort); if (Task.WhenAny(ct, Task.Delay(2000)).GetAwaiter().GetResult() == ct && sk.Connected) { SlowDnsLogger.Info("HysteriaEngine", "SOCKS5 TCP OK"); var stream = sk.GetStream(); stream.Write(new byte[] { 5, 1, 0 }, 0, 3); byte[] buf = new byte[2]; int n = stream.Read(buf, 0, 2); SlowDnsLogger.Info("HysteriaEngine", "SOCKS5 handshake: auth=" + (n == 2 ? buf[1].ToString() : "fail")); stream.Write(new byte[] { 5, 3, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 10); byte[] udpResp = new byte[10]; int un = stream.Read(udpResp, 0, 10); if (un >= 4 && udpResp[0] == 5 && udpResp[1] == 0) { int udpPort = (udpResp[8] << 8) | udpResp[9]; SlowDnsLogger.Info("HysteriaEngine", "UDP ASSOCIATE OK port=" + udpPort); } else SlowDnsLogger.Warn("HysteriaEngine", "UDP ASSOCIATE: rep=" + string.Join(",", udpResp)); } else SlowDnsLogger.Error("HysteriaEngine", "SOCKS5 port INACCESSIBLE"); } catch (Exception ex) { SlowDnsLogger.Error("HysteriaEngine", "SOCKS5 test error: " + ex.Message); }
            SlowDnsLogger.Info("HysteriaEngine", "Hysteria PRET port=" + _socksPort);
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
            try { SlowDnsLogger.Block("HysteriaEngine", "Config JSON", File.ReadAllText(path)); } catch { }
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

                lock (_captureLock) _hysteriaCaptured += e.Data + "\n";
                SlowDnsLogger.Raw("hysteria-stdout", e.Data);
                KighmuLogger.Info(TAG, $"[stdout] {e.Data}");
                bool looksConnected = lineLower.Contains("connected") ||
                    (lineLower.Contains("socks5") && e.Data.Contains("127.0.0.1:") && !e.Data.Contains("config:")) ||
                    (lineLower.Contains("udp") && (lineLower.Contains("session") || lineLower.Contains("running")));

                if (looksConnected && !loggedConnected)
                {
                    _serverConnected = true;
                    loggedConnected = true;
                    var match = Regex.Match(e.Data, @"127\.0\.0\.1:(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int port) && port > 0)
                        _socksPort = port;
                    KighmuLogger.Info(TAG, "Hysteria connecte");
                        SlowDnsLogger.Info("HysteriaEngine", "DETECTED CONNECTED: " + e.Data);
                }
                else if (lineLower.Contains("error") || lineLower.Contains("fatal"))
                {
                    KighmuLogger.Error(TAG, $"Hysteria: {e.Data}");
                        SlowDnsLogger.Error("HysteriaEngine", e.Data);
                }
            };

            // Brancher stderr sur la meme detection (Hysteria sort souvent sur stderr)
            _hysteriaProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null || !_running) return;
                string lineLowerErr = e.Data.ToLowerInvariant();
                lock (_captureLock) _hysteriaCaptured += e.Data + "\n";
                SlowDnsLogger.Raw("hysteria-stderr", e.Data);
                KighmuLogger.Info(TAG, $"[stderr] {e.Data}");
                bool looksConnectedErr = lineLowerErr.Contains("connected") ||
                    (lineLowerErr.Contains("socks5") && e.Data.Contains("127.0.0.1:") && !e.Data.Contains("config:")) ||
                    (lineLowerErr.Contains("udp") && (lineLowerErr.Contains("session") || lineLowerErr.Contains("running")));
                if (looksConnectedErr && !loggedConnected)
                {
                    _serverConnected = true;
                    loggedConnected = true;
                    var matchErr = Regex.Match(e.Data, @"127\.0\.0\.1:(\d+)");
                    if (matchErr.Success && int.TryParse(matchErr.Groups[1].Value, out int portErr) && portErr > 0)
                        _socksPort = portErr;
                    KighmuLogger.Info(TAG, "Hysteria connecte (stderr) \u2705");
                }
                else if (lineLowerErr.Contains("error") || lineLowerErr.Contains("fatal"))
                    KighmuLogger.Error(TAG, $"Hysteria(stderr): {e.Data}");
                        SlowDnsLogger.Error("HysteriaEngine", "[stderr] " + e.Data);
            };
            _hysteriaProcess.Exited += (s, e) =>
            {
                KighmuLogger.Info(TAG, $"Hysteria exit: {_hysteriaProcess?.ExitCode}");
                SlowDnsLogger.Error("HysteriaEngine", "PROCESS EXIT code=" + (_hysteriaProcess?.ExitCode ?? -99));
                _serverConnected = false;
            };

            _hysteriaProcess.Start();
            _hysteriaProcess.BeginOutputReadLine();
            _hysteriaProcess.BeginErrorReadLine();
            KighmuLogger.Info(TAG, "Hysteria PID demarre: " + _hysteriaProcess.Id);
            SlowDnsLogger.Info("HysteriaEngine", "Hysteria demarre PID=" + _hysteriaProcess.Id + " cmd=" + binary + " client --config " + configFile);
        }

        /// <summary>
        /// Démarre hev-socks5-tunnel pour router le trafic de l'adaptateur Wintun
        /// vers le SOCKS5 local (127.0.0.1:_socksPort).
        /// Remplace HevTun2Socks (lib native Android) par un process séparé.
        /// </summary>
        public void StartTun2Socks(string tunAdapterName)
        {
            KighmuLogger.Info(TAG, $"StartTun2Socks: adapter={tunAdapterName} socksPort={_socksPort} serverConnected={_serverConnected}");
            StartTun2SocksOnPort(tunAdapterName, _socksPort);
        }

        /// <summary>
        /// Variante permettant de forcer un port cible précis (utilisé par
        /// MultiHysteriaEngine pour pointer vers le port du SocksBalancer
        /// plutôt que le port direct de cet engine).
        /// </summary>
        public void StartTun2SocksOnPort(string tunAdapterName, int targetPort)
        {
            KighmuLogger.Info(TAG, $"StartTun2SocksOnPort: adapter={tunAdapterName} targetPort={targetPort}");
            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, TAG);
        }

        public async Task Stop()
        {
            SlowDnsLogger.Begin("HysteriaEngine", "STOP");
            _running = false;
            _serverConnected = false;
            SlowDnsLogger.Info("HysteriaEngine", "Arret Hysteria tunnel");
            try { SlowDnsLogger.Block("HysteriaEngine", "hysteria output final", _hysteriaCaptured); } catch { }
            try { var p = Process.Start(new ProcessStartInfo { FileName = "route", Arguments = "print -4", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (p != null) { string r = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); SlowDnsLogger.Block("HysteriaEngine", "Table routage APRES", r); } } catch { }

            await Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(); } catch { /* ignore */ }
                try { _hysteriaProcess?.Kill(); } catch { /* ignore */ }
            });

            _tun2socksProcess = null;
            _hysteriaProcess = null;
            SlowDnsLogger.Info("HysteriaEngine", "Hysteria arrete");
            KighmuLogger.Info(TAG, "Hysteria arrete");
        }

        public bool IsRunning() => _running && _serverConnected;
    }
}
