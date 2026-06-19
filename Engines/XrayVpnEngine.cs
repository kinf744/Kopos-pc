using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using Newtonsoft.Json;
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
    /// Equivalent de XrayVpnEngine.kt + XrayVpnEngineWrapper.kt.
    /// Lance xray.exe avec un config JSON genere depuis le profil,
    /// expose un SOCKS5 local, puis demarre tun2socks.
    /// </summary>
    public class XrayVpnEngine : ITunnelEngine
    {
        private const string TAG = "XrayVpnEngine";

        private readonly XrayVpnProfile _profile;
        private readonly int            _instanceId;

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

        private volatile bool _running;
        private Process?      _xrayProcess;
        private Process?      _tun2socksProcess;

        public XrayVpnEngine(XrayVpnProfile profile, int instanceId = 0)
        {
            _profile    = profile;
            _instanceId = instanceId;
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
            KighmuLogger.Info(TAG, "Demarrage XrayVpnEngine...");

            string configPath = WriteXrayConfig();
            string binary     = GetBinaryPath("xray.exe");
            if (!File.Exists(binary))
                throw new Exception("xray.exe introuvable dans bin/win");

            StartXrayProcess(binary, configPath);

            // Attendre que Xray soit pret (max 5s)
            bool ready = false;
            for (int i = 0; i < 25 && !ready; i++)
            {
                await Task.Delay(200);
                try
                {
                    using var s = new TcpClient();
                    var t = s.ConnectAsync("127.0.0.1", SocksPort);
                    if (await Task.WhenAny(t, Task.Delay(200)) == t && s.Connected)
                        ready = true;
                }
                catch { }
            }

            if (!ready)
                throw new Exception("Xray n'a pas demarre dans les temps");

            KighmuLogger.Info(TAG, $"XrayVpnEngine pret port={SocksPort}");
            return SocksPort;
        }

        private string WriteXrayConfig()
        {
            int socksPort = SocksPort;
            string jsonConfig = _profile.GetActiveJson();

            if (string.IsNullOrWhiteSpace(jsonConfig))
                jsonConfig = BuildXrayConfigFromProfile(socksPort);

            // Normalisation JSON : forcer port SOCKS5 local + listen 127.0.0.1
            try
            {
                var obj      = JObject.Parse(jsonConfig);
                var inbounds = obj["inbounds"] as JArray ?? new JArray();
                var cleaned  = new JArray();
                bool hasSocks = false;

                foreach (var ib in inbounds)
                {
                    var proto  = ib["protocol"]?.ToString() ?? "";
                    var listen = ib["listen"]?.ToString() ?? "127.0.0.1";
                    var port   = ib["port"]?.ToObject<int>() ?? 0;

                    if (listen == "0.0.0.0") ib["listen"] = "127.0.0.1";

                    if (proto == "socks")
                    {
                        if (port >= 10800 && port <= 10810)
                            _socksPort = port;
                        else
                            ib["port"] = socksPort;
                        ib["listen"] = "127.0.0.1";
                        hasSocks = true;
                    }
                    cleaned.Add(ib);
                }

                if (!hasSocks)
                {
                    cleaned.Add(JObject.Parse(
                        $"{{"listen":"127.0.0.1","port":{socksPort},"protocol":"socks","settings":{{"udp":true}}}}"));
                }

                obj["inbounds"] = cleaned;

                // Supprimer allowInsecure des tlsSettings
                var outbounds = obj["outbounds"] as JArray;
                if (outbounds != null)
                {
                    foreach (var ob in outbounds)
                    {
                        var ss  = ob["streamSettings"] as JObject;
                        var tls = ss?["tlsSettings"] as JObject;
                        if (tls != null)
                        {
                            tls.Remove("allowInsecure");
                            ss!["tlsSettings"] = tls;
                            ob["streamSettings"] = ss;
                        }
                    }
                    obj["outbounds"] = outbounds;
                }

                jsonConfig = obj.ToString(Formatting.Indented);
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur normalisation config: {ex.Message}");
            }

            string fileName = _instanceId == 0
                ? "xrayvpn_config.json"
                : $"xrayvpn_config_{_instanceId}.json";

            string dir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, jsonConfig);
            return path;
        }

        private string BuildTlsPart()
        {
            if (!_profile.Tls && string.IsNullOrWhiteSpace(_profile.PublicKey))
                return "";

            if (!string.IsNullOrWhiteSpace(_profile.PublicKey))
            {
                // Reality
                string fp  = string.IsNullOrWhiteSpace(_profile.Fingerprint) ? "chrome" : _profile.Fingerprint;
                string sid = string.IsNullOrWhiteSpace(_profile.ShortId)     ? "0000000000000000" : _profile.ShortId;
                string sn  = string.IsNullOrWhiteSpace(_profile.Sni)         ? _profile.ServerAddress : _profile.Sni;
                return $""security":"reality","realitySettings":{{"serverName":"{sn}","fingerprint":"{fp}","publicKey":"{_profile.PublicKey}","shortId":"{sid}"}}";
            }
            else
            {
                // TLS standard
                string sni      = string.IsNullOrWhiteSpace(_profile.Sni) ? _profile.ServerAddress : _profile.Sni;
                string insecure = _profile.AllowInsecure ? "true" : "false";
                string fp       = string.IsNullOrWhiteSpace(_profile.Fingerprint) ? "chrome" : _profile.Fingerprint;
                return $""security":"tls","tlsSettings":{{"serverName":"{sni}","allowInsecure":{insecure},"fingerprint":"{fp}"}}";
            }
        }

        private string BuildStreamSettings(string transport)
        {
            string net     = transport.ToLower();
            string tlsPart = BuildTlsPart();
            string security = tlsPart.Contains("reality") ? "reality"
                            : tlsPart.Contains("tls")     ? "tls"
                            : "none";

            string p       = string.IsNullOrWhiteSpace(_profile.WsPath) ? "/" : _profile.WsPath;
            string h       = string.IsNullOrWhiteSpace(_profile.WsHost) ? _profile.ServerAddress : _profile.WsHost;
            string grpcSvc = string.IsNullOrWhiteSpace(_profile.GrpcServiceName)
                           ? (string.IsNullOrWhiteSpace(_profile.WsPath) ? "/" : _profile.WsPath)
                           : _profile.GrpcServiceName;
            string kcpHdr  = string.IsNullOrWhiteSpace(_profile.KcpHeader) ? "none" : _profile.KcpHeader;

            string networkPart = net switch
            {
                "ws"                      => $""network":"ws","wsSettings":{{"path":"{p}","headers":{{"Host":"{h}"}}}}",
                "grpc"                    => $""network":"grpc","grpcSettings":{{"serviceName":"{grpcSvc}","multiMode":false}}",
                "xhttp" or "splithttp"    => $""network":"xhttp","xhttpSettings":{{"path":"{p}","host":"{h}","mode":"auto"}}",
                "h2" or "http"            => $""network":"h2","httpSettings":{{"path":"{p}","host":["{h}"]}}",
                "httpupgrade"             => $""network":"httpupgrade","httpupgradeSettings":{{"path":"{p}","host":"{h}"}}",
                "kcp" or "mkcp"           => $""network":"kcp","kcpSettings":{{"mtu":1350,"tti":20,"uplinkCapacity":5,"downlinkCapacity":20,"congestion":false,"readBufferSize":2,"writeBufferSize":2,"header":{{"type":"{kcpHdr}"}}}}",
                _                         => ""network":"tcp","tcpSettings":{}"
            };

            return !string.IsNullOrWhiteSpace(tlsPart)
                ? $"{{{networkPart},"security":"{security}",{tlsPart}}}"
                : $"{{{networkPart},"security":"none"}}";
        }

        private string BuildXrayConfigFromProfile(int socksPort)
        {
            string proto  = _profile.Protocol;
            string uuid   = _profile.Uuid;
            string host   = _profile.ServerAddress;
            int    port   = _profile.ServerPort;
            string stream = BuildStreamSettings(_profile.Transport);

            string outbound = proto switch
            {
                "vmess"  => $"{{"protocol":"vmess","settings":{{"vnext":[{{"address":"{host}","port":{port},"users":[{{"id":"{uuid}","alterId":0,"security":"auto"}}]}}]}},"streamSettings":{stream}}}",
                "trojan" => $"{{"protocol":"trojan","settings":{{"servers":[{{"address":"{host}","port":{port},"password":"{uuid}"}}]}},"streamSettings":{stream}}}",
                _        => $"{{"protocol":"vless","settings":{{"vnext":[{{"address":"{host}","port":{port},"users":[{{"id":"{uuid}","encryption":"none"}}]}}]}},"streamSettings":{stream}}}"
            };

            return $"{{"log":{{"loglevel":"warning"}},"inbounds":[{{"port":{socksPort},"listen":"127.0.0.1","protocol":"socks","settings":{{"udp":true}}}}],"outbounds":[{outbound},{{"protocol":"freedom","tag":"direct"}}],"routing":{{"rules":[]}}}}";
        }

        private void StartXrayProcess(string binary, string configPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = binary,
                Arguments              = $"run -c "{configPath}"",
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
                KighmuLogger.Info(TAG, "Xray VPN demarre");
            else if ((lower.Contains("error") || lower.Contains("fatal"))
                  && !lower.Contains("warning") && !lower.Contains("deprecated")
                  && !lower.Contains("connection reset") && !lower.Contains("broken pipe")
                  && !lower.Contains("eof") && !lower.Contains("use of closed")
                  && !lower.Contains("failed to dial"))
                KighmuLogger.Error(TAG, $"Xray: {line[..Math.Min(150, line.Length)]}");
        }

        public void StartTun2Socks(string tunAdapterName) =>
            StartTun2SocksOnPort(tunAdapterName, SocksPort);

        public void StartTun2SocksOnPort(string tunAdapterName, int targetPort)
        {
            try
            {
                string binary = GetBinaryPath("tun2socks.exe");
                if (!File.Exists(binary))
                {
                    KighmuLogger.Error(TAG, "tun2socks.exe introuvable");
                    return;
                }
                var psi = new ProcessStartInfo
                {
                    FileName               = binary,
                    Arguments              = $"-device wintun://{tunAdapterName} -proxy socks5://127.0.0.1:{targetPort}",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };
                _tun2socksProcess = new Process { StartInfo = psi };
                _tun2socksProcess.OutputDataReceived += (s, e) => { if (e.Data != null) KighmuLogger.Info("tun2socks", e.Data); };
                _tun2socksProcess.Start();
                _tun2socksProcess.BeginOutputReadLine();
                KighmuLogger.Info(TAG, $"tun2socks demarre port={targetPort}");
            }
            catch (Exception ex) { KighmuLogger.Error(TAG, $"tun2socks error: {ex.Message}"); }
        }

        public async Task Stop()
        {
            _running = false;
            await Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(true); } catch { }
                try
                {
                    _xrayProcess?.StandardInput.Close();
                    _xrayProcess?.Kill(true);
                } catch { }
            });
            _tun2socksProcess = null;
            _xrayProcess      = null;
            _socksPort        = 0;
            KighmuLogger.Info(TAG, "XrayVpnEngine arrete");
        }

        public bool IsRunning() => _running && (_xrayProcess != null && !_xrayProcess.HasExited);
    }
}
