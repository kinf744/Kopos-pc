using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using Newtonsoft.Json.Linq;
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
    /// Equivalent de XrayDnsEngine.kt.
    /// dnstt-client.exe (tunnel DNS) + xray.exe (proxy Xray connecte a dnstt en local).
    /// Xray se connecte a 127.0.0.1:dnsttPort sans TLS - dnstt fait le vrai tunnel.
    /// </summary>
    public class XrayDnsEngine : ITunnelEngine
    {
        private const string TAG = "XrayDnsEngine";

        private readonly XrayDnsProfile _profile;
        private readonly int            _instanceId;
        private readonly int            _externalDnsttPort;

        private int _socksPort;
        public int? GetSocksPort() => _socksPort > 0 ? _socksPort : (int?)null;
        private int SocksPort
        {
            get
            {
                if (_socksPort == 0) _socksPort = FindFreePort(10808 + _instanceId);
                return _socksPort;
            }
        }

        private int _dnsttPort;
        private int DnsttPort
        {
            get
            {
                if (_dnsttPort == 0)
                    _dnsttPort = _externalDnsttPort > 0
                        ? _externalDnsttPort
                        : FindFreePort(17000 + (_instanceId * 10));
                return _dnsttPort;
            }
        }

        private volatile bool _running;
        private string? _resolvedServerIp;
        public string? ServerIp => _resolvedServerIp;
        private Process?      _xrayProcess;
        private Process?      _dnsttProcess;
        private Process?      _tun2socksProcess;

        private string CleanPublicKey => _profile.PublicKey
            .Trim()
            .Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "")
            .Replace("(", "").Replace(")", "").Replace("'", "").Replace("\"", "")
            .Replace("`", "").Replace(";", "").Replace("&", "").Replace("|", "")
            .Replace("$", "");

        public XrayDnsEngine(XrayDnsProfile profile, int instanceId = 0, int externalDnsttPort = 0)
        {
            _profile           = profile;
            _instanceId        = instanceId;
            _externalDnsttPort = externalDnsttPort;
        }

        private static string GetBinaryPath(string name) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "win", name);

        private static bool IsPortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start(); l.Stop();
                return true;
            }
            catch { return false; }
        }

        private static int FindFreePort(int preferred)
        {
            for (int p = preferred; p <= preferred + 20; p++)
                if (IsPortFree(p)) return p;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public async Task<int> Start()
        {
            _running = true;
            KighmuLogger.Info(TAG, "Demarrage XrayDnsEngine (Mode 5)...");

            // Resoudre l'IP du resolveur DNS (cible reelle des paquets UDP de dnstt-client)
            // pour permettre son exclusion des routes systeme (evite boucle de routage)
            try
            {
                var entry = await System.Net.Dns.GetHostEntryAsync(_profile.DnsServer);
                _resolvedServerIp = entry.AddressList.Length > 0
                    ? entry.AddressList[0].ToString()
                    : _profile.DnsServer;
            }
            catch
            {
                _resolvedServerIp = _profile.DnsServer;
            }
            KighmuLogger.Info(TAG, $"IP resolveur DNS (dnstt): {_resolvedServerIp}");

            // ── Phase 1 : dnstt (sauf si port externe fourni) ───────────────────
            if (_externalDnsttPort > 0)
            {
                _dnsttPort = _externalDnsttPort;
                KighmuLogger.Info(TAG, $"dnstt externe utilise port={_externalDnsttPort}");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_profile.Nameserver))
                    throw new Exception("Nameserver manquant");
                if (string.IsNullOrWhiteSpace(CleanPublicKey))
                    throw new Exception("Public Key manquante");

                StartDnsttProcess();

                // Attendre dnstt pret (max 8s)
                bool dnsttReady = false;
                int  waited     = 0;
                while (waited < 8000)
                {
                    await Task.Delay(200);
                    waited += 200;
                    try
                    {
                        var s = new TcpClient();
                        var t = s.ConnectAsync("127.0.0.1", DnsttPort);
                        if (await Task.WhenAny(t, Task.Delay(100)) == t && s.Connected)
                        {
                            dnsttReady = true;
                            KighmuLogger.Info(TAG, $"dnstt pret en {waited}ms port={DnsttPort}");
                            break;
                        }
                    }
                    catch { }
                }
                if (!dnsttReady) throw new Exception("dnstt n'a pas demarre dans les temps");
            }

            // ── Phase 2 : Xray ───────────────────────────────────────────────────
            string configPath = WriteXrayConfig();
            string binary     = GetBinaryPath("xray.exe");
            if (!File.Exists(binary))
                throw new Exception("xray.exe introuvable dans bin/win");

            StartXrayProcess(binary, configPath);

            // Attendre Xray pret (max 5s)
            bool xrayReady = false;
            for (int i = 0; i < 25 && !xrayReady; i++)
            {
                await Task.Delay(200);
                try
                {
                    var s = new TcpClient();
                    var t = s.ConnectAsync("127.0.0.1", SocksPort);
                    if (await Task.WhenAny(t, Task.Delay(200)) == t && s.Connected)
                        xrayReady = true;
                }
                catch { }
            }
            if (!xrayReady) throw new Exception("Xray DNS n'a pas demarre dans les temps");

            KighmuLogger.Info(TAG, $"XrayDnsEngine pret port={SocksPort} dnsttPort={DnsttPort} serverIp={_resolvedServerIp}");
            return SocksPort;
        }

        private void StartDnsttProcess()
        {
            string bin = GetBinaryPath("dnstt-client.exe");
            if (!File.Exists(bin))
                throw new Exception("dnstt-client.exe introuvable dans bin/win");

            string args = $"-udp {_profile.DnsServer}:{_profile.DnsPort} -pubkey {CleanPublicKey} {_profile.Nameserver} 127.0.0.1:{DnsttPort}";

            string[] skipPatterns = {
                "begin stream", "opening stream", "handle: session", "closing stream",
                "stream timeout", "retransmit", "recv window", "send window", "keepalive",
                "end stream", "network is unreachable", "sendto:", "write udp", "accepted",
                "connection reset", "broken pipe", "copy stream", "copy local", "EOF",
                "MTU", "fingerprint", "session"
            };

            var psi = new ProcessStartInfo
            {
                FileName               = bin,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            _dnsttProcess = new Process { StartInfo = psi };
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

        private string WriteXrayConfig()
        {
            int    socksPort  = SocksPort;
            int    dnsttPort  = DnsttPort;
            string jsonConfig = _profile.XrayJsonConfig.Trim().StartsWith("{")
                ? _profile.XrayJsonConfig.Trim()
                : BuildXrayConfigFromProfile(socksPort, dnsttPort);

            // Normalisation : forcer connexion vers dnstt local, supprimer TLS
            try
            {
                var obj      = JObject.Parse(jsonConfig);
                var inbounds = obj["inbounds"] as JArray ?? new JArray();
                var cleaned  = new JArray();
                bool hasSocks = false;

                foreach (var ib in inbounds)
                {
                    string proto  = ib["protocol"]?.ToString() ?? "";
                    string listen = ib["listen"]?.ToString()   ?? "127.0.0.1";
                    if (listen == "0.0.0.0") ib["listen"] = "127.0.0.1";
                    if (proto == "socks")
                    {
                        ib["port"]   = socksPort;
                        ib["listen"] = "127.0.0.1";
                        hasSocks = true;
                    }
                    cleaned.Add(ib);
                }
                if (!hasSocks)
                    cleaned.Add(JObject.Parse(
                        $"{{\"listen\":\"127.0.0.1\",\"port\":{socksPort},\"protocol\":\"socks\",\"settings\":{{\"udp\":true}}}}"));

                obj["inbounds"] = cleaned;

                // Rediriger outbounds vers dnstt local, supprimer TLS/Reality
                var outbounds = obj["outbounds"] as JArray;
                if (outbounds != null)
                {
                    foreach (var ob in outbounds)
                    {
                        string proto = ob["protocol"]?.ToString() ?? "";
                        string tag   = ob["tag"]?.ToString()      ?? "";
                        if (proto == "freedom" || proto == "blackhole" || proto == "socks" || tag == "direct")
                            continue;

                        var settings = ob["settings"] as JObject;
                        // vmess / vless : vnext[0]
                        var vnext = settings?["vnext"] as JArray;
                        if (vnext != null && vnext.Count > 0)
                        {
                            vnext[0]["address"] = "127.0.0.1";
                            vnext[0]["port"]    = dnsttPort;
                        }
                        // trojan / shadowsocks : servers[0]
                        var servers = settings?["servers"] as JArray;
                        if (servers != null && servers.Count > 0)
                        {
                            servers[0]["address"] = "127.0.0.1";
                            servers[0]["port"]    = dnsttPort;
                        }
                        // Supprimer TLS - dnstt gere le chiffrement
                        var ss = ob["streamSettings"] as JObject;
                        if (ss != null)
                        {
                            ss["security"] = "none";
                            ss.Remove("tlsSettings");
                            ss.Remove("realitySettings");
                            ob["streamSettings"] = ss;
                        }
                    }
                    obj["outbounds"] = outbounds;
                }

                jsonConfig = obj.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur normalisation config: {ex.Message}");
            }

            string fileName = _instanceId == 0
                ? "xraydns_config.json"
                : $"xraydns_config_{_instanceId}.json";

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, jsonConfig);
            KighmuLogger.Info(TAG, $"Config Xray ecrite ({path}):\n{jsonConfig}");
            return path;
        }

        private string BuildStreamSettings(string transport)
        {
            string net     = transport.ToLower();
            string path    = string.IsNullOrWhiteSpace(_profile.WsPath) ? "/" : _profile.WsPath;
            string host    = string.IsNullOrWhiteSpace(_profile.WsHost) ? "127.0.0.1" : _profile.WsHost;
            string grpcSvc = string.IsNullOrWhiteSpace(_profile.WsPath) ? "/" : _profile.WsPath;

            return net switch
            {
                "ws"                   => $"{{\"network\":\"ws\",\"security\":\"none\",\"wsSettings\":{{\"path\":\"{path}\",\"headers\":{{\"Host\":\"{host}\"}}}}}}",
                "grpc"                 => $"{{\"network\":\"grpc\",\"security\":\"none\",\"grpcSettings\":{{\"serviceName\":\"{grpcSvc}\",\"multiMode\":false}}}}",
                "xhttp" or "splithttp" => $"{{\"network\":\"xhttp\",\"security\":\"none\",\"xhttpSettings\":{{\"path\":\"{path}\",\"host\":\"{host}\",\"mode\":\"auto\"}}}}",
                "h2" or "http"         => $"{{\"network\":\"h2\",\"security\":\"none\",\"httpSettings\":{{\"path\":\"{path}\",\"host\":[\"{host}\"]}}}}",
                "httpupgrade"          => $"{{\"network\":\"httpupgrade\",\"security\":\"none\",\"httpupgradeSettings\":{{\"path\":\"{path}\",\"host\":\"{host}\"}}}}",
                "kcp" or "mkcp"        => "{\"network\":\"kcp\",\"security\":\"none\",\"kcpSettings\":{\"mtu\":1350,\"tti\":20,\"uplinkCapacity\":5,\"downlinkCapacity\":20,\"congestion\":false,\"readBufferSize\":2,\"writeBufferSize\":2,\"header\":{\"type\":\"none\"}}}",
                _                      => "{\"network\":\"tcp\",\"security\":\"none\"}"
            };
        }

        private string BuildXrayConfigFromProfile(int socksPort, int dnsttPort)
        {
            string proto  = _profile.Protocol;
            string uuid   = _profile.Uuid;
            string stream = BuildStreamSettings(_profile.Transport);

            string outbound = proto switch
            {
                "vmess"  => $"{{\"protocol\":\"vmess\",\"settings\":{{\"vnext\":[{{\"address\":\"127.0.0.1\",\"port\":{dnsttPort},\"users\":[{{\"id\":\"{uuid}\",\"alterId\":0,\"security\":\"auto\"}}]}}]}},\"streamSettings\":{stream}}}",
                "trojan" => $"{{\"protocol\":\"trojan\",\"settings\":{{\"servers\":[{{\"address\":\"127.0.0.1\",\"port\":{dnsttPort},\"password\":\"{uuid}\"}}]}},\"streamSettings\":{stream}}}",
                _        => $"{{\"protocol\":\"vless\",\"settings\":{{\"vnext\":[{{\"address\":\"127.0.0.1\",\"port\":{dnsttPort},\"users\":[{{\"id\":\"{uuid}\",\"encryption\":\"none\"}}]}}]}},\"streamSettings\":{stream}}}"
            };

            return $"{{\"log\":{{\"loglevel\":\"warning\"}},\"inbounds\":[{{\"port\":{socksPort},\"listen\":\"127.0.0.1\",\"protocol\":\"socks\",\"settings\":{{\"udp\":true}}}}],\"outbounds\":[{outbound},{{\"protocol\":\"freedom\",\"tag\":\"direct\"}}],\"routing\":{{\"rules\":[]}}}}";
        }

        private void StartXrayProcess(string binary, string configPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = binary,
                Arguments              = $"run -c \"{configPath}\"",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            _xrayProcess = new Process { StartInfo = psi };
            _xrayProcess.OutputDataReceived += (s, e) => ProcessXrayLine(e.Data);
            _xrayProcess.ErrorDataReceived  += (s, e) => ProcessXrayLine(e.Data);
            _xrayProcess.Start();
            _xrayProcess.BeginOutputReadLine();
            _xrayProcess.BeginErrorReadLine();
        }

        private void ProcessXrayLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 500) return;
            string lower = line.ToLower();
            if (lower.Contains("started") && lower.Contains("xray"))
                KighmuLogger.Info(TAG, "Xray DNS demarre");
            else if ((lower.Contains("error") || lower.Contains("fatal"))
                  && !lower.Contains("warning") && !lower.Contains("deprecated")
                  && !lower.Contains("connection reset") && !lower.Contains("broken pipe")
                  && !lower.Contains("eof") && !lower.Contains("failed to dial"))
                KighmuLogger.Error(TAG, $"Xray: {line.Substring(0, Math.Min(150, line.Length))}");
        }

        public void StartTun2Socks(string tunAdapterName) =>
            StartTun2SocksOnPort(tunAdapterName, SocksPort);

        public void StartTun2SocksOnPort(string tunAdapterName, int targetPort)
        {
            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, TAG);
        }

        public async Task Stop()
        {
            _running = false;
            // Arret nucleaire : timeout 3s max
            var stopTask = Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(); } catch { }
                try { _xrayProcess?.Kill(); }     catch { }
                try { _dnsttProcess?.Kill(); }    catch { }
            });
            await Task.WhenAny(stopTask, Task.Delay(3000));
            _tun2socksProcess = null;
            _xrayProcess      = null;
            _dnsttProcess     = null;
            _socksPort        = 0;
            _dnsttPort        = 0;
            KighmuLogger.Info(TAG, "XrayDnsEngine arrete");
        }

        public bool IsRunning() => _running
            && (_xrayProcess  != null && !_xrayProcess.HasExited)
            && (_dnsttProcess == null || !_dnsttProcess.HasExited);
    }
}
