using KighmuVpnWindows.Models;
using KighmuVpnWindows.Utils;
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
    /// dnstt-client.exe (tunnel DNS) + plink.exe (SSH PuTTY) pour SOCKS5 dynamique.
    /// plink est l'outil standard pour SlowDNS sur Windows, bien plus fiable que SSH.NET.
    /// </summary>
    public class SlowDnsEngine : ITunnelEngine
    {
        private string? _resolvedServerIp;
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
        public bool HasDnsProxy => _dnsProxy != null;

        private Process? _plinkProcess;
        private Process? _dnsttProcess;
        private Process? _tun2socksProcess;
        private DnsProxy? _dnsProxy;
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
            SlowDnsLogger.Begin(TAG, "START tunnel SlowDNS");
            SlowDnsLogger.Info(TAG, $"Profile: ssh={_sshUser}@{_dns.SshHost}:{_dns.SshPort} dns={_dns.DnsServer}:{_dns.DnsPort} ns={_dns.Nameserver}");
            SlowDnsLogger.Info(TAG, $"PublicKey: {CleanPublicKey?.Substring(0, Math.Min(32, CleanPublicKey?.Length ?? 0))}...");
            SlowDnsLogger.Info(TAG, $"SocksPort: {SocksPort}, DnsttPort: {DnsttPort}");

            // Dump etat reseau avant demarrage
            try
            {
                var psi = new ProcessStartInfo { FileName = "route", Arguments = "print -4", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                var p = Process.Start(psi);
                string routeTable = p!.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                SlowDnsLogger.Block(TAG, "Table de routage AVANT", routeTable);
            }
            catch { }

            _running = true;
            _cts = new CancellationTokenSource();

            if (string.IsNullOrWhiteSpace(_dns.Nameserver)) throw new Exception("Nameserver manquant");
            if (string.IsNullOrWhiteSpace(CleanPublicKey)) throw new Exception("Public Key manquante");

            // Phase 0 : demarrer le proxy DNS local (DNS bypass tunnel)
            try
            {
                string? physIp = DetectPhysicalIp();
                var dnsServers = DetectSystemDnsServers();
                if (!string.IsNullOrEmpty(physIp) && dnsServers.Count > 0)
                {
                    string upstreamDns = dnsServers[0];
                    int upstreamPort = 53;
                    _dnsProxy = new DnsProxy(upstreamDns, upstreamPort, physIp, 53);
                    _dnsProxy.Start();
                    SlowDnsLogger.Info(TAG, $"DnsProxy: 127.0.0.1:53 -> {upstreamDns}:{upstreamPort} (bind={physIp})");
                }
                else
                    SlowDnsLogger.Warn(TAG, $"DnsProxy: impossible detecter IP physique={physIp ?? "(null)"} DNS={dnsServers.Count}");
            }
            catch (Exception ex)
            {
                SlowDnsLogger.Warn(TAG, $"DnsProxy start error: {ex.Message}");
                try { _dnsProxy?.Dispose(); } catch { }
                _dnsProxy = null;
            }

            // Phase 1 : démarrer dnstt
            if (_dnsttProcess == null || _dnsttProcess.HasExited)
            {
                string dnsttBin = GetBinaryPath("dnstt-client.exe");
                if (!File.Exists(dnsttBin)) throw new Exception("dnstt-client.exe introuvable dans bin/win");
                StartDnsttProcess(dnsttBin);

                int waited = 0;
                while (waited < 12000)
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
                    catch { }
                }
            }

            // Pre-cache SSH host key (plink exit 1 si clé inconnue)
            await CacheSshHostKey();

            // Phase 2 : plink (remplace SSH.NET)
            await StartPlink();

            SlowDnsLogger.Info(TAG, $"Tunnel SlowDNS OK: SOCKS5 port={SocksPort}");
            SlowDnsLogger.Block(TAG, "Etat apres demarrage", $"Tunnel SOCKS: port={SocksPort}, dnstt running={_dnsttProcess?.HasExited == false}, plink running={_plinkProcess?.HasExited == false}");

            // Tester le port SOCKS5
            try
            {
                using var sock = new System.Net.Sockets.TcpClient();
                var conn = sock.ConnectAsync(System.Net.IPAddress.Loopback, SocksPort);
                if (System.Threading.Tasks.Task.WhenAny(conn, System.Threading.Tasks.Task.Delay(2000)).GetAwaiter().GetResult() == conn && sock.Connected)
                {
                    SlowDnsLogger.Info(TAG, $"SOCKS5 port {SocksPort}: TCP OK");
                    // Test SOCKS5 handshake
                    var stream = sock.GetStream();
                    byte[] greets = new byte[] { 5, 1, 0 };
                    stream.Write(greets, 0, 3);
                    byte[] resp = new byte[2];
                    int nread = stream.Read(resp, 0, 2);
                    if (nread == 2 && resp[0] == 5)
                        SlowDnsLogger.Info(TAG, $"SOCKS5 port {SocksPort}: handshake OK (auth={resp[1]})");
                    else
                        SlowDnsLogger.Warn(TAG, $"SOCKS5 port {SocksPort}: handshake invalide (lu={nread} rep={string.Join(",", resp)})");
                }
                else
                    SlowDnsLogger.Error(TAG, $"SOCKS5 port {SocksPort}: INACCESSIBLE");
            }
            catch (Exception ex) { SlowDnsLogger.Error(TAG, $"SOCKS5 test echoue: {ex.Message}"); }

            return SocksPort;
        }

        public async Task<int> StartDnsttOnly()
        {
            _running = true;
            if (string.IsNullOrWhiteSpace(_dns.Nameserver)) throw new Exception("Nameserver manquant");
            if (string.IsNullOrWhiteSpace(CleanPublicKey)) throw new Exception("Public Key manquante");

            string dnsttBin = GetBinaryPath("dnstt-client.exe");
            if (!File.Exists(dnsttBin)) throw new Exception("dnstt-client.exe introuvable dans bin/win");
            StartDnsttProcess(dnsttBin);

            bool ready = false;
            int waited = 0;
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
                catch { }
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
            _dnsttProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null || !_running) return;
                bool skip = false;
                foreach (var p in skipPatterns)
                    if (e.Data.Contains(p)) { skip = true; break; }
                if (!skip) KighmuLogger.Info(TAG, $"dnstt-err: {e.Data}");
            };

            _dnsttProcess.Start();
            _dnsttProcess.BeginOutputReadLine();
            _dnsttProcess.BeginErrorReadLine();

            Thread.Sleep(500);
            if (_dnsttProcess.HasExited)
                throw new Exception($"dnstt crashed (exit={_dnsttProcess.ExitCode})");
        }

        private async Task CacheSshHostKey()
        {
            // plink 0.82+ envoie les invites sur CON, pas sur stdin/stdout/stderr.
            // On utilise -legacy-stdio-prompts pour forcer le passage par stdin,
            // et on pipe "y" pour accepter/cacher la clé SSH hôte.
            try
            {
                string plinkBin = GetBinaryPath("plink.exe");
                if (!File.Exists(plinkBin) || DnsttPort <= 0)
                {
                    KighmuLogger.Info(TAG, "CacheSshHostKey: plink introuvable ou port invalide, skip");
                    return;
                }

                string args = $"-legacy-stdio-prompts -l {_sshUser} -pw \"{_sshPass}\" -P {DnsttPort} -no-antispoof -N 127.0.0.1";
                SlowDnsLogger.Begin(TAG, "CacheSshHostKey");
                SlowDnsLogger.Info(TAG, "CacheSshHostKey: verification/cache de la cle SSH hote...");
                KighmuLogger.Info(TAG, "CacheSshHostKey: verification/cache de la cle SSH hote...");

                var psi = new ProcessStartInfo
                {
                    FileName = plinkBin,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                var cachedOutput = new System.Text.StringBuilder();
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) cachedOutput.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) cachedOutput.AppendLine(e.Data); };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Envoyer "y" pour accepter la clé si elle n'est pas encore en cache
                proc.StandardInput.WriteLine("y");
                proc.StandardInput.Flush();

                if (proc.WaitForExit(8000))
                {
                    string output = cachedOutput.ToString();
                    if (output.IndexOf("host key", StringComparison.OrdinalIgnoreCase) >= 0)
                        KighmuLogger.Info(TAG, "CacheSshHostKey: cle hote acceptee/cachee");
                    else if (proc.ExitCode == 0)
                        KighmuLogger.Info(TAG, "CacheSshHostKey: deja en cache (connexion rapide OK)");
                    else
                        KighmuLogger.Warning(TAG, $"CacheSshHostKey: plink exit={proc.ExitCode} (attendue si deja en cache)");
                }
                else
                {
                    try { proc.Kill(); } catch { }
                    KighmuLogger.Info(TAG, "CacheSshHostKey: timeout (cle probablement deja en cache, plink attend)");
                }
            }
            catch (Exception ex)
            {
                KighmuLogger.Warning(TAG, $"CacheSshHostKey: {ex.Message}");
            }
        }

        private async Task StartPlink()
        {
            // Configurer keepalive SSH dans le registre PuTTY (pas d'option CLI)
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\SimonTatham\PuTTY\Sessions\Default%20Settings");
                if (key != null)
                {
                    key.SetValue("PingInterval", 30, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("PingIntervalSecs", 30, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { /* keepalive non essentiel */ }

            string plinkBin = GetBinaryPath("plink.exe");
            if (!File.Exists(plinkBin))
                throw new Exception("plink.exe introuvable dans bin/win");

            int port = SocksPort;

            // Verifier que le port dnstt est bien joignable avant de lancer plink
            bool dnsttReady = false;
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    var s = new TcpClient();
                    var t = s.ConnectAsync(IPAddress.Loopback, DnsttPort);
                    if (await Task.WhenAny(t, Task.Delay(500)) == t && s.Connected)
                    {
                        s.Close();
                        dnsttReady = true;
                        break;
                    }
                }
                catch { }
                await Task.Delay(200);
            }
            if (!dnsttReady)
                KighmuLogger.Warning(TAG, $"dnstt port={DnsttPort} pas joignable, plink va probablement echouer");

            string args = $"-D {port} -P {DnsttPort} -l {_sshUser} -pw \"{_sshPass}\" -2 -C -v -no-antispoof -batch -N 127.0.0.1";
            string safeArgs = args.Replace(_sshPass, "***");

            SlowDnsLogger.Begin(TAG, "plink");
            SlowDnsLogger.Info(TAG, $"plink binaire: {plinkBin}");
            SlowDnsLogger.Info(TAG, $"plink args: -D {port} -P {DnsttPort} -l {_sshUser} -pw *** -2 -C -v -no-antispoof -batch -N 127.0.0.1");
            SlowDnsLogger.Info(TAG, $"SOCKS5 attendu sur port {port}");
            SlowDnsLogger.Info(TAG, $"Connexion SSH -> 127.0.0.1:{DnsttPort} via dnstt");

            // Verifier que le port dnstt repond
            try
            {
                using var precheck = new System.Net.Sockets.TcpClient();
                var pc = precheck.ConnectAsync(System.Net.IPAddress.Loopback, DnsttPort);
                if (System.Threading.Tasks.Task.WhenAny(pc, System.Threading.Tasks.Task.Delay(2000)).GetAwaiter().GetResult() == pc && precheck.Connected)
                {
                    precheck.Close();
                    SlowDnsLogger.Info(TAG, $"Pre-check: port dnstt {DnsttPort} accessible");
                }
                else
                    SlowDnsLogger.Error(TAG, $"Pre-check: port dnstt {DnsttPort} INJOIGNABLE");
            }
            catch (Exception ex) { SlowDnsLogger.Error(TAG, $"Pre-check dnstt: {ex.Message}"); }

            KighmuLogger.Info(TAG, $"Lancement plink: {plinkBin} -D {port} -P {DnsttPort} -l {_sshUser} -pw *** -2 -C -v -no-antispoof -batch -N 127.0.0.1");

            var psi = new ProcessStartInfo
            {
                FileName = plinkBin,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            _plinkProcess = new Process { StartInfo = psi };
            string capturedOutput = "";
            object outputLock = new();
            _plinkProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null || !_running) return;
                lock (outputLock) capturedOutput += e.Data + "\n";
                SlowDnsLogger.Raw("plink", e.Data);
                if (!e.Data.Contains("lastlogon") && !e.Data.Contains("password"))
                    KighmuLogger.Info(TAG, $"plink: {e.Data}");
            };
            _plinkProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null || !_running) return;
                lock (outputLock) capturedOutput += e.Data + "\n";
                SlowDnsLogger.Raw("plink-err", e.Data);
                if (e.Data.Contains("FATAL") || e.Data.Contains("ERROR") || e.Data.Contains("fatal"))
                {
                    KighmuLogger.Error(TAG, $"plink error: {e.Data}");
                    IsDegraded = true;
                }
                else if (!e.Data.Contains("lastlogon") && !e.Data.Contains("password"))
                {
                    KighmuLogger.Info(TAG, $"plink: {e.Data}");
                }
            };
            _plinkProcess.Exited += (s, e) =>
            {
                SlowDnsLogger.Error(TAG, $"plink EXIT code={_plinkProcess?.ExitCode}");
                _sshAlive = false;
            };

            _plinkProcess.Start();
            _plinkProcess.BeginOutputReadLine();
            _plinkProcess.BeginErrorReadLine();

            // Petit delai pour laisser plink emettre ses messages de diagnostic
            await Task.Delay(1000);

            // Verifier si plink est deja mort
            if (_plinkProcess.HasExited)
            {
                string dump;
                lock (outputLock) dump = capturedOutput;
                KighmuLogger.Error(TAG, $"plink mort au demarrage (exit={_plinkProcess.ExitCode}). Sortie plink:\n{dump}");
                throw new Exception($"plink mort au demarrage (exit={_plinkProcess.ExitCode})");
            }

            // Attendre que plink ouvre le port SOCKS5 (max 120s)
            KighmuLogger.Info(TAG, "Attente port SOCKS5 plink...");
            SlowDnsLogger.Info(TAG, $"Attente SOCKS5 port={port} (max 120s)");
            var socksWaitStart = DateTime.Now;
            bool socksReady = false;
            for (int i = 0; i < 239; i++)
            {
                await Task.Delay(500);

                if (_plinkProcess.HasExited)
                {
                    string dump;
                    lock (outputLock) dump = capturedOutput;
                    KighmuLogger.Error(TAG, $"plink mort pendant attente (exit={_plinkProcess.ExitCode}). Sortie plink:\n{dump}");
                    SlowDnsLogger.Error(TAG, $"plink MORT pendant attente SOCKS5 (exit={_plinkProcess.ExitCode}, t={i * 500}ms)");
                    SlowDnsLogger.Block(TAG, "Sortie plink", dump);
                    throw new Exception($"plink mort pendant attente (exit={_plinkProcess.ExitCode})");
                }

                // Toutes les 5 secondes, log un point de progression
                if (i > 0 && i % 10 == 0)
                    SlowDnsLogger.Info(TAG, $"Attente SOCKS5... {i * 500}ms ecoulees");

                try
                {
                    var s = new TcpClient();
                    var t = s.ConnectAsync(IPAddress.Loopback, port);
                    if (await Task.WhenAny(t, Task.Delay(200)) == t && s.Connected)
                    {
                        s.Close();
                        socksReady = true;
                        long elapsed = (long)(DateTime.Now - socksWaitStart).TotalMilliseconds;
                        KighmuLogger.Info(TAG, $"plink SOCKS5 pret port={port} en {i * 500}ms");
                        SlowDnsLogger.Info(TAG, $"SOCKS5 PRET: port={port} en {elapsed}ms");
                        break;
                    }
                }
                catch { }
            }

            if (!socksReady)
            {
                string dump;
                lock (outputLock) dump = capturedOutput;
                KighmuLogger.Error(TAG, $"plink n'a pas ouvert le port SOCKS5 dans les 120s. Sortie plink:\n{dump}");
                throw new Exception($"plink n'a pas ouvert le port SOCKS5 dans les 120s");
            }

            SlowDnsLogger.Info(TAG, "SSH alive = true (SOCKS5 ready)");
            _sshAlive = true;

            var token = _cts!.Token;

            // Surveillance plink
            _ = Task.Run(async () =>
            {
                while (_running && !token.IsCancellationRequested)
                {
                    await Task.Delay(8000, token).ContinueWith(_ => { });
                    if (!_running) break;
                    if (_plinkProcess == null || _plinkProcess.HasExited)
                    {
                        KighmuLogger.Error(TAG, "plink: processus mort");
                        _sshAlive = false;
                        break;
                    }
                }
            }, token);

            // Health check dnstt
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
                        var ct = s.ConnectAsync(IPAddress.Loopback, DnsttPort);
                        alive = await Task.WhenAny(ct, Task.Delay(1000)) == ct && s.Connected;
                    }
                    catch { alive = false; }

                    if (!alive)
                    {
                        dnsttFailCount++;
                        KighmuLogger.Error(TAG, $"dnstt health check echoue ({dnsttFailCount}/2)");
                        if (dnsttFailCount >= 2)
                        {
                            KighmuLogger.Error(TAG, "dnstt mort → IsDegraded=true");
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
            SlowDnsLogger.Begin(TAG, "tun2socks");
            SlowDnsLogger.Info(TAG, $"tun2socks: adapter={tunAdapterName} port={targetPort} udp=disabled mtu=1200");
            // SlowDNS: UDP desactive (SSH ne supporte pas UDP), MTU reduit (tunnel DNS lent)
            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, TAG, udpEnabled: false, mtu: 1200);
            if (_tun2socksProcess == null)
                SlowDnsLogger.Error(TAG, "tun2socks: echec demarrage (retour null)");
            else
                SlowDnsLogger.Info(TAG, $"tun2socks demarre PID={_tun2socksProcess.Id}");
        }

        public void StopDnsttOnly()
        {
            SlowDnsLogger.Info(TAG, "StopDnsttOnly: arret dnstt");
            try { _dnsttProcess?.Kill(); } catch { }
            _dnsttProcess = null;
            _dnsttPort = 0;
        }

        public void StopPlinkOnly()
        {
            SlowDnsLogger.Info(TAG, "StopPlinkOnly: arret plink");
            _sshAlive = false;
            try { _plinkProcess?.Kill(); } catch { }
            _plinkProcess = null;
            _socksPort = 0;
        }

        public async Task Stop()
        {
            SlowDnsLogger.Begin(TAG, "STOP");
            _running = false;
            _sshAlive = false;
            try { _cts?.Cancel(); } catch { }

            var stopTask = Task.Run(() =>
            {
                if (_tun2socksProcess != null) { SlowDnsLogger.Info(TAG, "Arret tun2socks..."); try { _tun2socksProcess.Kill(); } catch { } }
                if (_plinkProcess != null)     { SlowDnsLogger.Info(TAG, "Arret plink..."); try { _plinkProcess.Kill(); } catch { } }
                if (_dnsttProcess != null)     { SlowDnsLogger.Info(TAG, "Arret dnstt..."); try { _dnsttProcess.Kill(); } catch { } }
            });
            await Task.WhenAny(stopTask, Task.Delay(3000));

            _tun2socksProcess = null;
            _plinkProcess     = null;
            _dnsttProcess     = null;
            _dnsttPort        = 0;
            _socksPort        = 0;

            if (_dnsProxy != null)
            {
                SlowDnsLogger.Info(TAG, "Arret DnsProxy...");
                _dnsProxy.Dispose();
                _dnsProxy = null;
            }
            SlowDnsLogger.Info(TAG, "Tunnel SlowDNS stoppe");
            KighmuLogger.Info(TAG, "SlowDNS arrete");
        }

        public bool IsRunning() => _running && _sshAlive;

        private string? DetectPhysicalIp()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "route",
                    Arguments = "print -4 0.0.0.0",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
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

        private List<string> DetectSystemDnsServers()
        {
            var servers = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-Command \"Get-DnsClientServerAddress -AddressFamily IPv4 | Select-Object -ExpandProperty ServerAddresses\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                string output = p!.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                foreach (var line in output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var ip = line.Trim();
                    if (!string.IsNullOrEmpty(ip) && System.Net.IPAddress.TryParse(ip, out _))
                        if (!servers.Contains(ip)) servers.Add(ip);
                }
            }
            catch { }
            if (servers.Count == 0)
                servers.AddRange(new[] { "8.8.8.8", "8.8.4.4", "1.1.1.1" });
            return servers;
        }
    }
}
