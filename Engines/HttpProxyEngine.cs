using KighmuVpnWindows.Utils;
using Renci.SshNet;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Equivalent de HttpProxyEngine.kt.
    /// Connexion TCP au proxy HTTP, injection payload, bridge SSH.NET SOCKS5 dynamique.
    /// </summary>
    public class HttpProxyEngine : ITunnelEngine
    {
        private string? _resolvedServerIp;
        /// <summary>IP serveur (proxy) a exclure des routes systeme.</summary>
        public string? ServerIp => _resolvedServerIp;

        private const string TAG = "HttpProxyEngine";
        private const string CRLF = "\r\n";
        private const int PIPE_BUFFER_SIZE = 131072;

        // Config proxy
        private readonly string _proxyHost;
        private readonly int    _proxyPort;
        private readonly string _customPayload;

        // Config SSH
        private readonly string _sshHost;
        private readonly int    _sshPort;
        private readonly string _sshUser;
        private readonly string _sshPass;

        private readonly int _profileIndex;

        private int _socksPort;
        public int? GetSocksPort() => _socksPort > 0 ? _socksPort : (int?)null;
        private int SocksPort
        {
            get
            {
                if (_socksPort == 0) _socksPort = FindFreePort(10801 + _profileIndex);
                return _socksPort;
            }
        }

        private volatile bool _running;
        private volatile bool _sshAlive;

        private SshClient?            _sshClient;
        private ForwardedPortDynamic? _forwardedPort;
        private TcpClient?            _proxyTcpClient;
        private Process?              _tun2socksProcess;
        private CancellationTokenSource? _cts;

        public HttpProxyEngine(
            string proxyHost, int proxyPort, string customPayload,
            string sshHost,   int sshPort,   string sshUser, string sshPass,
            int profileIndex = 0)
        {
            _proxyHost     = proxyHost;
            _proxyPort     = proxyPort;
            _customPayload = customPayload;
            _sshHost       = sshHost;
            _sshPort       = sshPort;
            _sshUser       = sshUser;
            _sshPass       = sshPass;
            _profileIndex  = profileIndex;
        }

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

        private static string GetBinaryPath(string name) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "win", name);

        public async Task<int> Start()
        {
            _running = true;
            SlowDnsLogger.Begin("HttpProxyEngine", "START HTTP-Proxy tunnel");
            try { var pi = new ProcessStartInfo { FileName = "route", Arguments = "print -4", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }; var pr = Process.Start(pi); string rt = pr!.StandardOutput.ReadToEnd(); pr.WaitForExit(3000); SlowDnsLogger.Block("HttpProxyEngine", "Table de routage AVANT", rt); } catch { }
            _cts = new CancellationTokenSource();

            if (string.IsNullOrWhiteSpace(_proxyHost)) throw new Exception("Proxy Host manquant");
            if (string.IsNullOrWhiteSpace(_sshHost))   throw new Exception("SSH Host manquant");
            if (string.IsNullOrWhiteSpace(_sshUser))   throw new Exception("SSH Username manquant");

            // ── Phase 1 : Connexion TCP au proxy ────────────────────────────────
            var tcpClient = new TcpClient();
            tcpClient.ReceiveBufferSize = PIPE_BUFFER_SIZE;
            tcpClient.SendBufferSize    = PIPE_BUFFER_SIZE;
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            tcpClient.Client.NoDelay = true;

            try
            {
                var entry = await System.Net.Dns.GetHostEntryAsync(_proxyHost);
                _resolvedServerIp = entry.AddressList.Length > 0
                    ? entry.AddressList[0].ToString()
                    : _proxyHost;
            }
            catch
            {
                _resolvedServerIp = _proxyHost;
            }
            KighmuLogger.Info(TAG, $"IP serveur proxy: {_resolvedServerIp}");

            await tcpClient.ConnectAsync(_proxyHost, _proxyPort);
            _proxyTcpClient = tcpClient;

            var netStream  = tcpClient.GetStream();
            var rawPayload = string.IsNullOrWhiteSpace(_customPayload)
                ? "CONNECT [host]:[port] HTTP/1.1[crlf]Host: [host]:[port][crlf]Proxy-Connection: Keep-Alive[crlf][crlf]"
                : _customPayload;

            var payload = rawPayload
                .Replace("[host]",       _sshHost)
                .Replace("[HOST]",       _sshHost)
                .Replace("[real_host]",  _sshHost)
                .Replace("[REAL_HOST]",  _sshHost)
                .Replace("[port]",       _sshPort.ToString())
                .Replace("[PORT]",       _sshPort.ToString())
                .Replace("[proxy_host]", _proxyHost)
                .Replace("[proxy_port]", _proxyPort.ToString())
                .Replace("[crlf]",       CRLF)
                .Replace("[CRLF]",       CRLF)
                .Replace("[cr]",         "\r")
                .Replace("[lf]",         "\n")
                .Replace("\\r\\n",  CRLF)
                .Replace("\\r",       "\r")
                .Replace("\\n",       "\n");

            // ── Envoi payload (split / delay / normal) ───────────────────────────
            SendPayload(netStream, payload, rawPayload);

            // ── Lecture réponse proxy ────────────────────────────────────────────
            bool isConnect = rawPayload.TrimStart().StartsWith("CONNECT", StringComparison.OrdinalIgnoreCase);
            string firstLine = ReadHttpLine(netStream);
            KighmuLogger.Info(TAG, $"Response: {firstLine}");

            bool isError = firstLine.Contains("400") || firstLine.Contains("403") ||
                           firstLine.Contains("407") || firstLine.Contains("502") ||
                           firstLine.Contains("404") || firstLine.Contains("500");

            if (isConnect && !firstLine.Contains("200") && !firstLine.Contains("101"))
            {
                ConsumeHeaders(netStream);
                throw new Exception($"Proxy CONNECT refuse: {firstLine}");
            }
            if (isError)
            {
                ConsumeHeaders(netStream);
                throw new Exception($"Proxy erreur: {firstLine}");
            }
            ConsumeHeaders(netStream);

            // ── Phase 2 : Bridge SSH.NET sur le flux proxy ───────────────────────
            // On expose le flux proxy via un ServerSocket local pour que SSH.NET s'y connecte
            var bridgeSS   = new TcpListener(IPAddress.Loopback, 0);
            bridgeSS.Start();
            int bridgePort = ((IPEndPoint)bridgeSS.LocalEndpoint).Port;

            var versionLatch = new SemaphoreSlim(0, 1);

            _ = Task.Run(() =>
            {
                try
                {
                    var bridgeClient = bridgeSS.AcceptTcpClient();
                    bridgeSS.Stop();
                    bridgeClient.ReceiveBufferSize = PIPE_BUFFER_SIZE;
                    bridgeClient.SendBufferSize    = PIPE_BUFFER_SIZE;
                    bridgeClient.Client.NoDelay    = true;

                    var realIn      = netStream;
                    var bridgeStream = bridgeClient.GetStream();

                    // Lire la bannière SSH et la retransmettre à SSH.NET
                    var versionBytes = new StringBuilder();
                    int b;
                    while ((b = realIn.ReadByte()) != -1)
                    {
                        versionBytes.Append((char)b);
                        if (versionBytes.ToString().EndsWith("\n")) break;
                    }
                    string banner = versionBytes.ToString().Trim();
                    if (!string.IsNullOrEmpty(banner)) KighmuLogger.Info(TAG, banner);

                    byte[] bannerBytes = Encoding.UTF8.GetBytes(versionBytes.ToString());
                    bridgeStream.Write(bannerBytes, 0, bannerBytes.Length);
                    bridgeStream.Flush();
                    versionLatch.Release();

                    // Pipe bidirectionnel
                    var t1 = new Thread(() => { try { Pipe(realIn, bridgeStream); } catch { } }) { IsBackground = true };
                    var t2 = new Thread(() => { try { Pipe(bridgeStream, realIn); } catch { } }) { IsBackground = true };
                    t1.Start(); t2.Start();
                }
                catch { versionLatch.Release(); }
            });

            await versionLatch.WaitAsync(TimeSpan.FromSeconds(5));

            // ── Phase 3 : SSH.NET → SOCKS5 dynamique ────────────────────────────
            var connInfo = new ConnectionInfo("127.0.0.1", bridgePort, _sshUser,
                new PasswordAuthenticationMethod(_sshUser, _sshPass))
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            var client = new SshClient(connInfo);
            client.Connect();
            if (!client.IsConnected) throw new Exception($"SSH auth echoue pour {_sshUser}");
            KighmuLogger.Info(TAG, "Auth complete");

            int socksPort = SocksPort;
            var forwarder = new ForwardedPortDynamic("127.0.0.1", (uint)socksPort);
            client.AddForwardedPort(forwarder);
            forwarder.Start();

            _sshClient     = client;
            _forwardedPort = forwarder;
            _sshAlive      = true;

            // Keep-alive
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (_running && !token.IsCancellationRequested)
                {
                    await Task.Delay(8000, token).ContinueWith(_ => { });
                    if (!_running) break;
                    try
                    {
                        if (!client.IsConnected) { _sshAlive = false; break; }
                        client.SendKeepAlive();
                    }
                    catch { _sshAlive = false; break; }
                }
            }, token);

            SlowDnsLogger.Info("HttpProxyEngine", "HTTP Proxy SOCKS5 ready port=" + socksPort);
            try { using var sk = new System.Net.Sockets.TcpClient(); var ct = sk.ConnectAsync(System.Net.IPAddress.Loopback, socksPort); if (System.Threading.Tasks.Task.WhenAny(ct, System.Threading.Tasks.Task.Delay(2000)).GetAwaiter().GetResult() == ct && sk.Connected) { SlowDnsLogger.Info("HttpProxyEngine", "SOCKS5 test: port=" + socksPort + " OK"); var stream = sk.GetStream(); stream.Write(new byte[] { 5, 1, 0 }, 0, 3); byte[] buf = new byte[2]; int n = stream.Read(buf, 0, 2); SlowDnsLogger.Info("HttpProxyEngine", "SOCKS5 handshake: auth=" + (n == 2 ? buf[1].ToString() : "fail")); } else SlowDnsLogger.Warn("HttpProxyEngine", "SOCKS5 test: INACCESSIBLE"); } catch (Exception ex) { SlowDnsLogger.Warn("HttpProxyEngine", "SOCKS5 test error: " + ex.Message); }
            KighmuLogger.Info(TAG, $"HTTP Proxy prêt SOCKS5 port={socksPort}");
            return socksPort;
        }

        private void SendPayload(Stream stream, string payload, string raw)
        {
            if (raw.IndexOf("[split]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var parts = payload.Split(new[] { "[split]" }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++)
                {
                    byte[] data = Encoding.GetEncoding("ISO-8859-1").GetBytes(parts[i]);
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                    if (i < parts.Length - 1) Thread.Sleep(30);
                }
            }
            else if (raw.IndexOf("[delay]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var lines = payload.Split(new[] { CRLF }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = i < lines.Length - 1 ? lines[i] + CRLF : lines[i];
                    byte[] data = Encoding.GetEncoding("ISO-8859-1").GetBytes(line);
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                    Thread.Sleep(20);
                }
            }
            else
            {
                byte[] data = Encoding.GetEncoding("ISO-8859-1").GetBytes(payload);
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
        }

        private string ReadHttpLine(Stream stream)
        {
            var sb   = new StringBuilder();
            int prev = -1;
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) break;
                if (prev == '\r' && b == '\n')
                {
                    if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
                    break;
                }
                if (b == '\n') break;
                sb.Append((char)b);
                prev = b;
            }
            return sb.ToString();
        }

        private void ConsumeHeaders(Stream stream)
        {
            string h;
            do { h = ReadHttpLine(stream); } while (!string.IsNullOrEmpty(h));
        }

        private void Pipe(Stream input, Stream output)
        {
            var buf = new byte[PIPE_BUFFER_SIZE];
            try
            {
                while (true)
                {
                    int n = input.Read(buf, 0, buf.Length);
                    if (n == 0) break;
                    output.Write(buf, 0, n);
                    output.Flush();
                }
            }
            catch { }
        }

        public void StartTun2Socks(string tunAdapterName) =>
            StartTun2SocksOnPort(tunAdapterName, SocksPort);

        public void StartTun2SocksOnPort(string tunAdapterName, int targetPort)
        {
            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, TAG);
        }

        public async Task Stop()
        {
            SlowDnsLogger.Begin("HttpProxyEngine", "STOP");
            _running  = false;
            _sshAlive = false;
            try { _cts?.Cancel(); } catch { }
            // Arret avec timeout 3s max (deconnexion nucleaire)
            var stopTask = Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(); } catch { }
                try { _proxyTcpClient?.Close(); }      catch { }
                try { _forwardedPort?.Stop(); }        catch { }
                try { _sshClient?.Disconnect(); _sshClient?.Dispose(); } catch { }
            });
            await Task.WhenAny(stopTask, Task.Delay(3000));
            _tun2socksProcess = null;
            _sshClient        = null;
            KighmuLogger.Info(TAG, "HttpProxyEngine arrêté");
        }

        public bool IsRunning() => _running && _sshAlive;
    }
}
