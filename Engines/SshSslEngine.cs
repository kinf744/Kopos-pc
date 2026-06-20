using KighmuVpnWindows.Models;
using KighmuVpnWindows.Utils;
using Renci.SshNet;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Equivalent de SshSslEngine (AllEngines.kt).
    /// Connexion TLS vers le serveur SSH, bridge local, SSH.NET SOCKS5 dynamique.
    /// </summary>
    public class SshSslEngine : ITunnelEngine
    {
        private const string TAG = "SshSslEngine";

        private readonly SshSslConfig _sslConfig;
        private readonly int          _profileIndex;

        private int _socksPort;
        public int? GetSocksPort() => _socksPort > 0 ? _socksPort : (int?)null;
        private int SocksPort
        {
            get
            {
                if (_socksPort == 0) _socksPort = FindFreePort(10804 + _profileIndex);
                return _socksPort;
            }
        }

        private volatile bool _running;
        private volatile bool _sshAlive;

        private SshClient?            _sshClient;
        private ForwardedPortDynamic? _forwardedPort;
        private SslStream?            _sslStream;
        private TcpClient?            _tlsTcpClient;
        private Process?              _tun2socksProcess;
        private CancellationTokenSource? _cts;

        public SshSslEngine(SshSslConfig sslConfig, int profileIndex = 0)
        {
            _sslConfig    = sslConfig;
            _profileIndex = profileIndex;
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
            _cts     = new CancellationTokenSource();

            if (string.IsNullOrWhiteSpace(_sslConfig.SshHost))
                throw new Exception("SSH Host manquant");
            if (string.IsNullOrWhiteSpace(_sslConfig.SshUser))
                throw new Exception("SSH Username manquant");

            KighmuLogger.Info(TAG, "Demarrage tunnel SSH SSL/TLS...");

            // ── Phase 1 : Connexion TLS vers le serveur ──────────────────────────
            var sslStream = await BuildSslStreamAsync();
            _sslStream    = sslStream;
            KighmuLogger.Info(TAG, "SSL handshake OK");

            // ── Phase 2 : Bridge local TCP ↔ SslStream ───────────────────────────
            int bridgePort = FindFreePort(19000 + _profileIndex);
            var bridgeSS   = new TcpListener(IPAddress.Loopback, bridgePort);
            bridgeSS.Start();

            _ = Task.Run(() =>
            {
                try
                {
                    var client = bridgeSS.AcceptTcpClient();
                    bridgeSS.Stop();
                    client.Client.NoDelay = true;
                    var clientStream = client.GetStream();

                    var t1 = new Thread(() =>
                    {
                        try { Pipe(clientStream, sslStream); } catch { }
                        try { client.Close(); }    catch { }
                        try { sslStream.Close(); } catch { }
                    }) { IsBackground = true };

                    var t2 = new Thread(() =>
                    {
                        try { Pipe(sslStream, clientStream); } catch { }
                        try { client.Close(); }    catch { }
                        try { sslStream.Close(); } catch { }
                    }) { IsBackground = true };

                    t1.Start(); t2.Start();
                }
                catch (Exception ex)
                {
                    KighmuLogger.Error(TAG, $"Bridge error: {ex.Message}");
                    try { bridgeSS.Stop(); } catch { }
                }
            });

            // ── Phase 3 : Bridge banniere SSH (lire SSH-2.0-xxx et le retransmettre) ──
            int bannerPort  = FindFreePort(19100 + _profileIndex);
            var bannerSS    = new TcpListener(IPAddress.Loopback, bannerPort);
            bannerSS.Start();

            var bannerLatch = new SemaphoreSlim(0, 1);

            _ = Task.Run(() =>
            {
                try
                {
                    bannerLatch.Release();
                    var trileadSock = bannerSS.AcceptTcpClient();
                    bannerSS.Stop();
                    trileadSock.Client.NoDelay = true;

                    var realSock = new TcpClient();
                    realSock.Connect("127.0.0.1", bridgePort);
                    realSock.Client.NoDelay = true;

                    var realIn      = realSock.GetStream();
                    var trileadStream = trileadSock.GetStream();

                    // Lire la banniere SSH et la retransmettre
                    var bannerBytes = new StringBuilder();
                    int b;
                    while ((b = realIn.ReadByte()) != -1)
                    {
                        bannerBytes.Append((char)b);
                        if (bannerBytes.ToString().EndsWith("\n")) break;
                    }
                    string banner = bannerBytes.ToString().Trim();
                    if (!string.IsNullOrEmpty(banner))
                        KighmuLogger.Info(TAG, banner);

                    byte[] bannerData = Encoding.UTF8.GetBytes(bannerBytes.ToString());
                    trileadStream.Write(bannerData, 0, bannerData.Length);
                    trileadStream.Flush();

                    var t1 = new Thread(() => { try { Pipe(realIn, trileadStream); } catch { } }) { IsBackground = true };
                    var t2 = new Thread(() => { try { Pipe(trileadStream, realIn); } catch { } }) { IsBackground = true };
                    t1.Start(); t2.Start();
                }
                catch (Exception ex)
                {
                    KighmuLogger.Error(TAG, $"BannerProxy error: {ex.Message}");
                    try { bannerSS.Stop(); } catch { }
                    bannerLatch.Release();
                }
            });

            await bannerLatch.WaitAsync(TimeSpan.FromSeconds(3));

            // ── Phase 4 : SSH.NET → SOCKS5 dynamique ────────────────────────────
            var connInfo = new ConnectionInfo("127.0.0.1", bannerPort, _sslConfig.SshUser,
                new PasswordAuthenticationMethod(_sslConfig.SshUser, _sslConfig.SshPass))
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var client2 = new SshClient(connInfo);
            client2.Connect();
            if (!client2.IsConnected)
                throw new Exception($"SSH auth echoue pour {_sslConfig.SshUser}");

            KighmuLogger.Info(TAG, "Auth complete");

            int socksPort = SocksPort;
            var forwarder = new ForwardedPortDynamic("127.0.0.1", (uint)socksPort);
            client2.AddForwardedPort(forwarder);
            forwarder.Start();

            _sshClient     = client2;
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
                        if (!client2.IsConnected) { _sshAlive = false; break; }
                        client2.SendKeepAlive();
                    }
                    catch { _sshAlive = false; break; }
                }
            }, token);

            KighmuLogger.Info(TAG, $"SSH SSL/TLS connecte port={socksPort}");
            return socksPort;
        }

        private async Task<SslStream> BuildSslStreamAsync()
        {
            string host = _sslConfig.SshHost;
            int    port = _sslConfig.SshPort > 0 ? _sslConfig.SshPort : 443;

            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port);
            _tlsTcpClient = tcpClient;

            var sslStream = new SslStream(
                tcpClient.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (sender, cert, chain, errors) => true // allowInsecure
            );

            string sni = string.IsNullOrWhiteSpace(_sslConfig.Sni) ? host : _sslConfig.Sni;

            var tlsOptions = new SslClientAuthenticationOptions
            {
                TargetHost                          = sni,
                EnabledSslProtocols                 = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
            };

            await sslStream.AuthenticateAsClientAsync(tlsOptions);
            return sslStream;
        }

        private static void Pipe(Stream input, Stream output)
        {
            var buf = new byte[16384];
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
            _running  = false;
            _sshAlive = false;
            try { _cts?.Cancel(); } catch { }
            await Task.Run(() =>
            {
                try { _tun2socksProcess?.Kill(true); } catch { }
                try { _forwardedPort?.Stop(); }        catch { }
                try { _sshClient?.Disconnect(); _sshClient?.Dispose(); } catch { }
                try { _sslStream?.Close(); }           catch { }
                try { _tlsTcpClient?.Close(); }        catch { }
            });
            _tun2socksProcess = null;
            _sshClient        = null;
            KighmuLogger.Info(TAG, "SshSslEngine arrete");
        }

        public bool IsRunning() => _running && _sshAlive;
    }
}
