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
        private string? _resolvedServerIp;
        /// <summary>IP serveur (Xray) a exclure des routes systeme.</summary>
        public string? ServerIp => _resolvedServerIp;

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
            SlowDnsLogger.Begin("XrayVpnEngine", "START Xray VPN tunnel");
            KighmuLogger.Info(TAG, "Demarrage XrayVpnEngine...");

            try
            {
                var entry = await System.Net.Dns.GetHostEntryAsync(_profile.ServerAddress);
                _resolvedServerIp = entry.AddressList.Length > 0
                    ? entry.AddressList[0].ToString()
                    : _profile.ServerAddress;
            }
            catch
            {
                _resolvedServerIp = _profile.ServerAddress;
            }
            KighmuLogger.Info(TAG, $"IP serveur Xray: {_resolvedServerIp}");

            string configPath = WriteXrayConfig();
            string binary     = GetBinaryPath("xray.exe");
            if (!File.Exists(binary))
                throw new Exception("xray.exe introuvable dans bin/win");

            StartXrayProcess(binary, configPath);

            // Attendre que Xray soit pret (max 8s)
            bool ready = false;
            bool procExited = false;
            for (int i = 0; i < 40 && !ready && !procExited; i++)
            {
                await Task.Delay(200);
                procExited = _xrayProcess != null && _xrayProcess.HasExited;
                if (procExited)
                {
                    KighmuLogger.Error(TAG, $"xray.exe s'est arrete prematurement code={_xrayProcess?.ExitCode}");
                    break;
                }
                try
                {
                    var s = new TcpClient();
                    var t = s.ConnectAsync("127.0.0.1", SocksPort);
                    if (await Task.WhenAny(t, Task.Delay(200)) == t && s.Connected)
                    {
                        ready = true;
                        KighmuLogger.Info(TAG, $"xray.exe pret port={SocksPort} apres {i * 200}ms");
                    }
                }
                catch { }
            }

            if (!ready && !procExited)
            {
                // Verifier le process une derniere fois
                bool alive = _xrayProcess != null && !_xrayProcess.HasExited;
                KighmuLogger.Error(TAG, $"Xray pas pret apres 8s (process alive={alive})");
                throw new Exception("Xray n'a pas demarre a temps");
            }
            else if (!ready && procExited)
            {
                throw new Exception("xray.exe s'est arrete immediatement (verifiez le fichier config)");
            }

            SlowDnsLogger.Info("XrayVpnEngine", "Xray VPN SOCKS5 ready port=" + SocksPort);
            try { using var sk = new System.Net.Sockets.TcpClient(); var ct = sk.ConnectAsync(System.Net.IPAddress.Loopback, SocksPort); if (System.Threading.Tasks.Task.WhenAny(ct, System.Threading.Tasks.Task.Delay(2000)).GetAwaiter().GetResult() == ct && sk.Connected) { SlowDnsLogger.Info("XrayVpnEngine", "SOCKS5 test: port=" + SocksPort + " OK"); var stream = sk.GetStream(); stream.Write(new byte[] { 5, 1, 0 }, 0, 3); byte[] buf = new byte[2]; int n = stream.Read(buf, 0, 2); SlowDnsLogger.Info("XrayVpnEngine", "SOCKS5 handshake: auth=" + (n == 2 ? buf[1].ToString() : "fail")); } else SlowDnsLogger.Warn("XrayVpnEngine", "SOCKS5 test: INACCESSIBLE"); } catch (Exception ex) { SlowDnsLogger.Warn("XrayVpnEngine", "SOCKS5 test error: " + ex.Message); }
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
                        $"{{\"listen\":\"127.0.0.1\",\"port\":{socksPort},\"protocol\":\"socks\",\"settings\":{{\"udp\":true}}}}"));
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
            SlowDnsLogger.Block("XrayVpnEngine", "Config JSON", jsonConfig);
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
                return $"\"security\":\"reality\",\"realitySettings\":{{\"serverName\":\"{sn}\",\"fingerprint\":\"{fp}\",\"publicKey\":\"{_profile.PublicKey}\",\"shortId\":\"{sid}\"}}";
            }
            else
            {
                // TLS standard
                string sni      = string.IsNullOrWhiteSpace(_profile.Sni) ? _profile.ServerAddress : _profile.Sni;
                string insecure = _profile.AllowInsecure ? "true" : "false";
                string fp       = string.IsNullOrWhiteSpace(_profile.Fingerprint) ? "chrome" : _profile.Fingerprint;
                return $"\"security\":\"tls\",\"tlsSettings\":{{\"serverName\":\"{sni}\",\"allowInsecure\":{insecure},\"fingerprint\":\"{fp}\"}}"; 
            }
        }

                private string BuildStreamSettings(string transport)
        {
            string net     = transport.ToLower();
            string tlsPart = BuildTlsPart();

            string p       = string.IsNullOrWhiteSpace(_profile.WsPath) ? "/" : _profile.WsPath;
            string h       = string.IsNullOrWhiteSpace(_profile.WsHost) ? _profile.ServerAddress : _profile.WsHost;
            string grpcSvc = string.IsNullOrWhiteSpace(_profile.GrpcServiceName)
                           ? (string.IsNullOrWhiteSpace(_profile.WsPath) ? "/" : _profile.WsPath)
                           : _profile.GrpcServiceName;

            string networkPart = net switch
            {
                "ws"                      => $"\"network\":\"ws\",\"wsSettings\":{{\"path\":\"{p}\",\"headers\":{{\"Host\":\"{h}\"}}}}",
                "grpc"                    => $"\"network\":\"grpc\",\"grpcSettings\":{{\"serviceName\":\"{grpcSvc}\",\"multiMode\":false}}",
                "xhttp" or "splithttp"    => $"\"network\":\"xhttp\",\"xhttpSettings\":{{\"path\":\"{p}\",\"host\":\"{h}\",\"mode\":\"auto\"}}",
                "h2" or "http"            => $"\"network\":\"h2\",\"httpSettings\":{{\"path\":\"{p}\",\"host\":[\"{ h}\"]}}",
                "httpupgrade"             => $"\"network\":\"httpupgrade\",\"httpupgradeSettings\":{{\"path\":\"{p}\",\"host\":\"{h}\"}}",
                "kcp" or "mkcp"           => $"\"network\":\"mkcp\",\"kcpSettings\":{{\"mtu\":1350,\"tti\":20,\"uplinkCapacity\":5,\"downlinkCapacity\":20,\"congestion\":false,\"readBufferSize\":1,\"writeBufferSize\":1}}",
                _                         => "\"network\":\"tcp\",\"tcpSettings\":{}"
            };

            // BuildTlsPart() contient deja "security":"tls","tlsSettings":{...} (sans accolades exterieures)
            // networkPart aussi: paires cle-valeur sans {}. On wrappe le tout dans { }.
            return !string.IsNullOrWhiteSpace(tlsPart)
                ? $"{{{networkPart},{tlsPart}}}"
                : $"{{{networkPart},\"security\":\"none\"}}";
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
                "vmess"  => $"{{\"protocol\":\"vmess\",\"settings\":{{\"vnext\":[{{\"address\":\"{host}\",\"port\":{port},\"users\":[{{\"id\":\"{uuid}\",\"alterId\":0,\"security\":\"auto\"}}]}}]}},\"streamSettings\":{stream}}}",
                "trojan" => $"{{\"protocol\":\"trojan\",\"settings\":{{\"servers\":[{{\"address\":\"{host}\",\"port\":{port},\"password\":\"{uuid}\"}}]}},\"streamSettings\":{stream}}}",
                _        => $"{{\"protocol\":\"vless\",\"settings\":{{\"vnext\":[{{\"address\":\"{host}\",\"port\":{port},\"users\":[{{\"id\":\"{uuid}\",\"encryption\":\"none\"}}]}}]}},\"streamSettings\":{stream}}}"
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
            SlowDnsLogger.Raw("XrayVpnEngine", line);
            string lower = line.ToLower();
            if (lower.Contains("started") && lower.Contains("xray"))
                KighmuLogger.Info(TAG, "Xray VPN demarre");
            else if (lower.Contains("failed to dial"))
                KighmuLogger.Warning(TAG, $"Xray dial: {line.Substring(0, Math.Min(150, line.Length))}");
            else if ((lower.Contains("error") || lower.Contains("fatal"))
                  && !lower.Contains("warning") && !lower.Contains("deprecated")
                  && !lower.Contains("connection reset") && !lower.Contains("broken pipe")
                  && !lower.Contains("eof") && !lower.Contains("use of closed")
                  && !lower.Contains("successfully dialed") && !lower.Contains("404"))
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
            SlowDnsLogger.Begin("XrayVpnEngine", "STOP");
            _running = false;
            await Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(); } catch { }
                try
                {
                    _xrayProcess?.StandardInput.Close();
                    _xrayProcess?.Kill();
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
