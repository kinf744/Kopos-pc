using KighmuVpnWindows.Models;
using KighmuVpnWindows.Utils;
using Renci.SshNet;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Équivalent de SlowDnsEngine.kt.
    /// dnstt-client.exe (process) + SSH.NET (remplace trilead-ssh2) pour le port forwarding SOCKS5 dynamique.
    /// Pas de banner proxy bridge : SSH.NET gère nativement le handshake SSH sur 127.0.0.1:dnsttPort.
    /// </summary>
    public class SlowDnsEngine : ITunnelEngine
    {
        private string? _resolvedServerIp;
        /// <summary>IP serveur (resolver DNS) a exclure des routes systeme.</summary>
        public string? ServerIp => _resolvedServerIp;

        private const string TAG = "SlowDnsEngine";
        public const int BASE_SOCKS_PORT = 10800;

        private readonly SlowDnsConfig _dns;
        private readonly string _sshUser;
        private readonly string _sshPass;
        private readonly int _profileIndex;

        private int _socksPort;
        public int? GetSocksPort() => _socksPort > 0 ? _socksPort : (int?)null;
        private int SocksPort
        {
            get
            {
                if (_socksPort == 0) _socksPort = FindFreePort(BASE_SOCKS_PORT + _profileIndex);
                return _socksPort;
            }
        }

        private int _dnsttPort;
        private int DnsttPort
        {
            get
            {
                if (_dnsttPort == 0) _dnsttPort = FindFreePort(17000 + (_profileIndex * 10));
                return _dnsttPort;
            }
        }

        private volatile bool _running;
        private volatile bool _sshAlive;
        public volatile bool IsDegraded;

        private SshClient? _sshClient;
        private ForwardedPortDynamic? _forwardedPort;
        private Process? _dnsttProcess;
        private Process? _tun2socksProcess;
        private CancellationTokenSource? _cts;

        private string CleanPublicKey => (_dns.PublicKey ?? "")
            .Trim().Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "")
            .Replace("(", "").Replace(")", "").Replace("'", "").Replace("\"", "")
            .Replace("`", "").Replace(";", "").Replace("&", "").Replace("|", "").Replace("$", "");

        public SlowDnsEngine(SlowDnsConfig dns, string sshUser, string sshPass, int profileIndex = 0)
        {
            _dns = dns;
            _sshUser = sshUser;
            _sshPass = sshPass;
            _profileIndex = profileIndex;
        }

        private static bool IsPortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        private static int FindFreePort(int preferred)
        {
            for (int p = preferred; p <= preferred + 50; p++)
                if (IsPortFree(p)) return p;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            System.Threading.Thread.Sleep(100);
            return IsPortFree(port) ? port : FindFreePortRandom();
        }

        private static int FindFreePortRandom()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                System.Threading.Thread.Sleep(50);
                if (IsPortFree(port)) return port;
            }
            return new Random().Next(20000, 30000);
        }

        public async Task<int> Start()
        {
            _running = true;
            _cts = new CancellationTokenSource();

            if (string.IsNullOrWhiteSpace(_dns.Nameserver)) throw new Exception("Nameserver manquant");
            if (string.IsNullOrWhiteSpace(CleanPublicKey)) throw new Exception("Public Key manquante");

            // Phase 1 : démarrer dnstt seulement si pas déjà vivant
            if (_dnsttProcess == null || _dnsttProcess.HasExited)
            {
                string dnsttBin = GetBinaryPath("dnstt-client.exe");
                if (!File.Exists(dnsttBin)) throw new Exception("dnstt-client.exe introuvable dans bin/win");
                StartDnsttProcess(dnsttBin);

                // Attendre que dnstt soit prêt (max 8s, check toutes les 200ms)
                int waited = 0;
                while (waited < 8000)
                {
                    await Task.Delay(200);
                    waited += 200;
                    try
                    {
                        var sock = new TcpClient();
                        var connectTask = sock.ConnectAsync(IPAddress.Loopback, DnsttPort);
                        if (await Task.WhenAny(connectTask, Task.Delay(100)) == connectTask)
                        {
                            KighmuLogger.Info(TAG, $"dnstt prêt en {waited}ms");
                            break;
                        }
                    }
                    catch { /* pas encore prêt */ }
                }
            }

            // Phase 2 : SSH uniquement (rapide, retry possible sans relancer dnstt)
            StartSsh();

            return SocksPort;
        }

        /// <summary>Démarrer seulement dnstt sans SSH - pour usage par un engine composite (Xray+SlowDNS)</summary>
        public async Task<int> StartDnsttOnly()
        {
            _running = true;
            if (string.IsNullOrWhiteSpace(_dns.Nameserver)) throw new Exception("Nameserver manquant");
            if (string.IsNullOrWhiteSpace(CleanPublicKey)) throw new Exception("Public Key manquante");

            string dnsttBin = GetBinaryPath("dnstt-client.exe");
            if (!File.Exists(dnsttBin)) throw new Exception("dnstt-client.exe introuvable dans bin/win");
            StartDnsttProcess(dnsttBin);

            // Attendre que dnstt ecoute reellement (max 10s, check TCP toutes les 200ms)
            bool ready  = false;
            int  waited = 0;
            while (waited < 10000)
            {
                await Task.Delay(200);
                waited += 200;

                if (_dnsttProcess?.HasExited == true)
                    throw new Exception($"dnstt mort au demarrage (exit={_dnsttProcess.ExitCode})");

                try
                {
                    var s = new TcpClient();
                    var t = s.ConnectAsync(IPAddress.Loopback, DnsttPort);
                    if (await Task.WhenAny(t, Task.Delay(100)) == t && s.Connected)
                    {
                        s.Close();
                        ready = true;
                        KighmuLogger.Info(TAG, $"dnstt (XrayDns) pret en {waited}ms port={DnsttPort}");
                        break;
                    }
                }
                catch { /* pas encore pret */ }
            }

            if (!ready)
                throw new Exception($"dnstt n'a pas demarre dans les temps (port={DnsttPort})");

            return DnsttPort;
        }

        private static string GetBinaryPath(string name) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "win", name);

        private void StartDnsttProcess(string bin)
        {
            try
            {
                var entry = System.Net.Dns.GetHostEntry(_dns.DnsServer);
                _resolvedServerIp = entry.AddressList.Length > 0
                    ? entry.AddressList[0].ToString()
                    : _dns.DnsServer;
            }
            catch
            {
                _resolvedServerIp = _dns.DnsServer;
            }
            KighmuLogger.Info(TAG, $"IP resolveur DNS (dnstt): {_resolvedServerIp}");

            string args = $"-udp {_dns.DnsServer}:{_dns.DnsPort} -pubkey {CleanPublicKey} {_dns.Nameserver} 127.0.0.1:{DnsttPort}";

            var psi = new ProcessStartInfo
            {
                FileName = bin,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _dnsttProcess = new Process { StartInfo = psi };

            string[] skipPatterns = {
                "begin stream", "opening stream", "handle: session", "closing stream",
                "stream timeout", "retransmit", "recv window", "send window", "keepalive",
                "end stream", "network is unreachable", "sendto:", "write udp", "accepted",
                "connection reset", "broken pipe", "copy stream", "copy local", "EOF"
            };

            _dnsttProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null || !_running) return;
                bool skip = false;
                foreach (var p in skipPatterns)
                    if (e.Data.Contains(p)) { skip = true; break; }
                if (!skip) KighmuLogger.Info(TAG, $"dnstt: {e.Data}");
            };

            _dnsttProcess.Start();
            _dnsttProcess.BeginOutputReadLine();
            _dnsttProcess.BeginErrorReadLine();

            Thread.Sleep(500);
            if (_dnsttProcess.HasExited)
                throw new Exception($"dnstt crashed (exit={_dnsttProcess.ExitCode})");
        }

        private void StartSsh()
        {
            // SSH.NET se connecte directement sur le flux exposé par dnstt - pas de proxy intermédiaire requis
            var connInfo = new ConnectionInfo("127.0.0.1", DnsttPort, _sshUser,
                new PasswordAuthenticationMethod(_sshUser, _sshPass))
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            var client = new SshClient(connInfo);
            client.Connect();

            if (!client.IsConnected) throw new Exception($"SSH auth echoue pour {_sshUser}");

            // SOCKS5 proxy local port libre garanti
            int port = SocksPort;
            var forwarder = new ForwardedPortDynamic("127.0.0.1", (uint)port);
            client.AddForwardedPort(forwarder);
            forwarder.Start();

            _sshClient = client;
            _forwardedPort = forwarder;
            _sshAlive = true;

            var token = _cts!.Token;

            // ── Keep-alive toutes les 8s avec détection de mort ─────────────────
            _ = Task.Run(async () =>
            {
                while (_running && !token.IsCancellationRequested)
                {
                    await Task.Delay(8000, token).ContinueWith(_ => { });
                    if (!_running) break;
                    try
                    {
                        if (!client.IsConnected)
                        {
                            KighmuLogger.Error(TAG, "Keep-alive: tunnel mort, marquage sshAlive=false");
                            _sshAlive = false;
                            break;
                        }
                        client.SendKeepAlive();
                    }
                    catch (Exception ex)
                    {
                        KighmuLogger.Error(TAG, $"Keep-alive erreur → tunnel mort: {ex.Message}");
                        _sshAlive = false;
                        break;
                    }
                }
            }, token);

            // ── Health check dnstt indépendant du SSH ────────────────────────────
            _ = Task.Run(async () =>
            {
                int dnsttFailCount = 0;
                while (_running && !token.IsCancellationRequested)
                {
                    await Task.Delay(15000, token).ContinueWith(_ => { });
                    if (!_running) break;

                    bool alive;
                    try
                    {
                        var s = new TcpClient();
                        var connectTask = s.ConnectAsync(IPAddress.Loopback, DnsttPort);
                        alive = await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask && s.Connected;
                    }
                    catch { alive = false; }

                    if (!alive)
                    {
                        dnsttFailCount++;
                        KighmuLogger.Error(TAG, $"dnstt health check échoué ({dnsttFailCount}/2)");
                        if (dnsttFailCount >= 2)
                        {
                            KighmuLogger.Error(TAG, "dnstt mort détecté → IsDegraded=true");
                            IsDegraded = true;
                            break;
                        }
                    }
                    else
                    {
                        dnsttFailCount = 0;
                    }
                }
            }, token);
        }

        public void StartTun2Socks(string tunAdapterName) => StartTun2SocksOnPort(tunAdapterName, SocksPort);

        public void StartTun2SocksOnPort(string tunAdapterName, int targetPort)
        {
            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, TAG);
        }

        /// <summary>Arrêter seulement dnstt - force nouveau port au prochain démarrage</summary>
        public void StopDnsttOnly()
        {
            try { _dnsttProcess?.Kill(); } catch { /* ignore */ }
            _dnsttProcess = null;
            _dnsttPort = 0;
        }

        /// <summary>Arrêter seulement SSH - garder dnstt vivant pour retry rapide</summary>
        public void StopSshOnly()
        {
            _sshAlive = false;
            try { _forwardedPort?.Stop(); } catch { /* ignore */ }
            try { _sshClient?.Disconnect(); _sshClient?.Dispose(); } catch { /* ignore */ }
            _sshClient = null;
            _socksPort = 0;
        }

        public async Task Stop()
        {
            _running = false;
            _sshAlive = false;
            try { _cts?.Cancel(); } catch { /* ignore */ }

            // Arret nucleaire : timeout 3s max
            var stopTask = Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(); } catch { /* ignore */ }
                try { _dnsttProcess?.Kill(); }    catch { /* ignore */ }
                try { _forwardedPort?.Stop(); }   catch { /* ignore */ }
                try { _sshClient?.Disconnect(); _sshClient?.Dispose(); } catch { /* ignore */ }
            });
            await Task.WhenAny(stopTask, Task.Delay(3000));

            _tun2socksProcess = null;
            _sshClient        = null;
            _dnsttProcess     = null;
            _dnsttPort        = 0;
            _socksPort        = 0;

            KighmuLogger.Info(TAG, "SlowDNS arrete");
        }

        public bool IsRunning() => _running && _sshAlive;
    }
}
