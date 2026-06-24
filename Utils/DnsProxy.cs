using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Utils
{
    public class DnsProxy : IDisposable
    {
        private UdpClient _server;
        private readonly IPEndPoint _upstream;
        private readonly IPAddress _bindAddress;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<ushort, IPEndPoint> _pending = new();

        /// <param name="upstreamIp">IP du vrai serveur DNS (ex: 8.8.8.8)</param>
        /// <param name="upstreamPort">Port du vrai serveur DNS (53)</param>
        /// <param name="bindIp">IP de l'interface physique (ex: 192.168.54.68) — force le trafic sortant par l'interface physique</param>
        /// <param name="listenPort">Port d'ecoute local (53 par defaut)</param>
        public DnsProxy(string upstreamIp, int upstreamPort, string bindIp, int listenPort = 53)
        {
            _upstream = new IPEndPoint(IPAddress.Parse(upstreamIp), upstreamPort);
            _bindAddress = IPAddress.Parse(bindIp);
            _server = new UdpClient(new IPEndPoint(IPAddress.Loopback, listenPort));
            _cts = new CancellationTokenSource();
        }

        public void Start()
        {
            _cts.Token.Register(() => { try { _server.Close(); } catch { } });
            _ = Task.Run(ReceiveLoop);
            KighmuLogger.Info("DnsProxy", $"Demarre sur 127.0.0.1:{((IPEndPoint)_server.Client.LocalEndPoint).Port} -> {_upstream} (bind={_bindAddress})");
        }

        private async Task ReceiveLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _server.ReceiveAsync(_cts.Token);
                    var query = result.Buffer;
                    var client = result.RemoteEndPoint;
                    if (query.Length < 12) continue;
                    ushort txId = (ushort)((query[0] << 8) | query[1]);
                    _pending[txId] = client;
                    _ = ForwardAsync(query, txId);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    KighmuLogger.Warning("DnsProxy", $"Receive: {ex.Message}");
                }
            }
        }

        private async Task ForwardAsync(byte[] query, ushort txId)
        {
            try
            {
                using var upstream = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                upstream.Bind(new IPEndPoint(_bindAddress, 0));
                upstream.Connect(_upstream);
                upstream.SendTimeout = 5000;

                await upstream.SendAsync(new ArraySegment<byte>(query), SocketFlags.None);

                var buffer = new byte[4096];
                var recv = await upstream.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
                byte[] response = new byte[recv];
                Array.Copy(buffer, 0, response, 0, recv);

                if (_pending.TryRemove(txId, out var client))
                    await _server.SendAsync(response, response.Length, client);
            }
            catch (Exception ex)
            {
                KighmuLogger.Warning("DnsProxy", $"Forward[{txId}]: {ex.Message}");
                _pending.TryRemove(txId, out _);
            }
        }

        public int ListenPort => ((IPEndPoint)_server.Client.LocalEndPoint).Port;

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            try { _server?.Close(); } catch { }
        }
    }
}
