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
                KighmuLogger.Info(TAG, $"DNS résolu: {_config.ServerAddress} -> {ip}");
                SlowDnsLogger.Info("HysteriaEngine", "DNS OK: " + _config.ServerAddress + " -> " + ip);
            }
            catch (Exception ex)
            {
                ip = _config.ServerAddress;
                KighmuLogger.Error(TAG, $"DIAG: Échec résolution DNS pour '{_config.ServerAddress}' — {ex.GetType().Name}: {ex.Message}");
                SlowDnsLogger.Error("HysteriaEngine", "DIAG DNS FAIL: " + ex.GetType().Name + ": " + ex.Message);
            }
            _resolvedServerIp = ip;

            string portHopping = string.IsNullOrWhiteSpace(_config.PortHopping)
                ? _config.ServerPort.ToString()
                : _config.PortHopping;
            string server = $"{ip}:{portHopping}";
            KighmuLogger.Info(TAG, $"Demarrage Hysteria: {server}");
            SlowDnsLogger.Info("HysteriaEngine", "Serveur cible: " + server + " socksPort=" + _socksPort + " auth=" + (_config.AuthPassword ?? "(none)") + " obfs=" + (_config.ObfsPassword ?? "(none)") + " up=" + _config.UploadMbps + " down=" + _config.DownloadMbps);

            string configFile = WriteConfig(server);
            string binary = GetBinaryPath("hysteria.exe");

            // DIAG: Vérification du binaire
            if (!File.Exists(binary))
                throw new Exception("hysteria.exe introuvable dans bin/win");
            var binInfo = new System.IO.FileInfo(binary);
            if (binInfo.Length == 0)
                throw new Exception("hysteria.exe corrompu (0 octets). Binaire invalide.");
            KighmuLogger.Info(TAG, $"Binaire OK: {binInfo.Length} octets, modifié le {binInfo.LastWriteTime}");
            SlowDnsLogger.Info("HysteriaEngine", "Binaire: " + binary + " (" + binInfo.Length + " octets)");
            try { var vi = Process.Start(new ProcessStartInfo { FileName = binary, Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); if (vi != null) { string vout = vi.StandardOutput.ReadToEnd(); vi.WaitForExit(2000); SlowDnsLogger.Block("HysteriaEngine", "Version hysteria", vout); } } catch (Exception exv) { SlowDnsLogger.Warn("HysteriaEngine", "Version check: " + exv.Message); }

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
                    int? exitCode = _hysteriaProcess?.ExitCode;
                    string diag = exitCode switch
                    {
                        -1073741515 => "BINAIRE INCOMPATIBLE (0xC0000135 — DLL manquante ou wrong architecture)",
                        -1073741701 => "BINAIRE INCOMPATIBLE (0xC000007B — not a valid Win32 application)",
                        -1073741819 => "BLOQUÉ PAR ANTI-VIRUS (0xC0000005 — access violation)",
                        -1073741502 => "BLOQUÉ PAR ANTI-VIRUS (0xC000013A — process terminated)",
                        1            => "ERREUR CONFIG (exit 1 — JSON invalide ou paramètre manquant)",
                        2            => "ERREUR CONFIG (exit 2 — fichier config introuvable ou illisible)",
                        null         => "PROCESSUS NON DÉMARRÉ (Process.Start a échoué)",
                        _            => $"EXIT CODE {exitCode}"
                    };
                    KighmuLogger.Error(TAG, $"DIAG: Hysteria process mort prématurément à {i*500}ms — {diag}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG: " + diag);
                    break;
                }
                if (i % 4 == 0) KighmuLogger.Info(TAG, $"Attente Hysteria... {i*500}ms serverConnected={_serverConnected} socksPort={_socksPort}");
                        SlowDnsLogger.Info("HysteriaEngine", "Attente " + (i*500) + "ms sc=" + _serverConnected + " sp=" + _socksPort);
                await Task.Delay(500);
            }

            SlowDnsLogger.Info("HysteriaEngine", "Fin attente: sc=" + _serverConnected + " sp=" + _socksPort + " a=" + (_hysteriaProcess != null && !_hysteriaProcess.HasExited));
            try { SlowDnsLogger.Block("HysteriaEngine", "hysteria output complet", _hysteriaCaptured); } catch { }

            if (!_serverConnected)
            {
                string raison = "connexion serveur impossible";
                if (_hysteriaProcess != null && _hysteriaProcess.HasExited)
                    raison = "processus hysteria.exe terminé avant connexion";
                else if (!string.IsNullOrWhiteSpace(_config.ServerAddress))
                    raison = $"serveur '{_config.ServerAddress}:{portHopping}' injoignable (timeout 15s)";
                KighmuLogger.Error(TAG, $"DIAG: {raison}");
                SlowDnsLogger.Error("HysteriaEngine", "DIAG: " + raison);
                throw new Exception($"Hysteria: {raison} (port={_socksPort})");
            }

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
                    string diag = DiagnostiquerErreurHysteria(e.Data, lineLower);
                    KighmuLogger.Error(TAG, $"Hysteria erreur: {e.Data} — {diag}");
                    SlowDnsLogger.Error("HysteriaEngine", "ERR: " + e.Data + " — " + diag);
                }
                else if (lineLower.Contains("json") || lineLower.Contains("parse") || lineLower.Contains("unmarshal"))
                {
                    KighmuLogger.Error(TAG, $"DIAG CONFIG: Erreur dans le fichier JSON de configuration — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG CONFIG: " + e.Data);
                }
                else if (lineLower.Contains("refused") || lineLower.Contains("timeout") || lineLower.Contains("no route"))
                {
                    KighmuLogger.Error(TAG, $"DIAG SERVEUR: Problème réseau — serveur injoignable — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG SERVEUR: " + e.Data);
                }
                else if (lineLower.Contains("certificate") || lineLower.Contains("tls") || lineLower.Contains("handshake"))
                {
                    KighmuLogger.Error(TAG, $"DIAG SERVEUR: Erreur TLS/certificat — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG SERVEUR TLS: " + e.Data);
                }
                else if (lineLower.Contains("permission") || lineLower.Contains("denied") || lineLower.Contains("access"))
                {
                    KighmuLogger.Error(TAG, $"DIAG PC: Blocage pare-feu/antivirus — permission refusée — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG PC: " + e.Data);
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
                {
                    string diag = DiagnostiquerErreurHysteria(e.Data, lineLowerErr);
                    KighmuLogger.Error(TAG, $"Hysteria erreur(stderr): {e.Data} — {diag}");
                    SlowDnsLogger.Error("HysteriaEngine", "ERR(stderr): " + e.Data + " — " + diag);
                }
                else if (lineLowerErr.Contains("json") || lineLowerErr.Contains("parse") || lineLowerErr.Contains("unmarshal"))
                {
                    KighmuLogger.Error(TAG, $"DIAG CONFIG: Erreur dans le fichier JSON de configuration — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG CONFIG: " + e.Data);
                }
                else if (lineLowerErr.Contains("refused") || lineLowerErr.Contains("timeout") || lineLowerErr.Contains("no route"))
                {
                    KighmuLogger.Error(TAG, $"DIAG SERVEUR: Problème réseau — serveur injoignable — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG SERVEUR: " + e.Data);
                }
                else if (lineLowerErr.Contains("certificate") || lineLowerErr.Contains("tls") || lineLowerErr.Contains("handshake"))
                {
                    KighmuLogger.Error(TAG, $"DIAG SERVEUR: Erreur TLS/certificat — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG SERVEUR TLS: " + e.Data);
                }
                else if (lineLowerErr.Contains("permission") || lineLowerErr.Contains("denied") || lineLowerErr.Contains("access"))
                {
                    KighmuLogger.Error(TAG, $"DIAG PC: Blocage pare-feu/antivirus — permission refusée — {e.Data}");
                    SlowDnsLogger.Error("HysteriaEngine", "DIAG PC: " + e.Data);
                }
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
        /// Diagnostique les erreurs hysteria pour catégoriser la cause racine.
        /// </summary>
        private static string DiagnostiquerErreurHysteria(string raw, string lower)
        {
            if (lower.Contains("json") || lower.Contains("parse") || lower.Contains("unmarshal") || lower.Contains("syntax"))
                return "CONFIG: JSON invalide dans le fichier de configuration";
            if (lower.Contains("refused"))
                return "SERVEUR: Connexion refusée — le serveur n'écoute pas sur ce port ou est down";
            if (lower.Contains("timeout"))
                return "SERVEUR: Timeout — le serveur ne répond pas (firewall ou panne)";
            if (lower.Contains("no route"))
                return "PC/RÉSEAU: Pas de route vers le serveur (pare-feu local ou hors-ligne)";
            if (lower.Contains("certificate") || lower.Contains("cert") || lower.Contains("tls") || lower.Contains("handshake"))
                return "SERVEUR: Problème de certificat TLS — serveur mal configuré";
            if (lower.Contains("permission") || lower.Contains("denied") || lower.Contains("access") || lower.Contains("blocked"))
                return "PC: Pare-feu ou antivirus bloque hysteria.exe";
            if (lower.Contains("protocol") || lower.Contains("version") || lower.Contains("incompatible"))
                return "BINAIRE/SERVEUR: Incompatibilité de version entre client et serveur hysteria";
            if (lower.Contains("dns"))
                return "PC/RÉSEAU: Échec de résolution DNS";
            if (lower.Contains("buffer") || lower.Contains("memory") || lower.Contains("alloc"))
                return "PC: Mémoire insuffisante ou allocation échouée";
            if (lower.Contains("socket"))
                return "PC: Impossible de créer un socket (pare-feu ou limite système)";
            return "Cause non identifiée — voir le message ci-dessus";
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
