using KighmuVpnWindows.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Équivalent exact de SocksBalancer.kt.
    /// Répartit les connexions entrantes sur plusieurs ports SOCKS5 locaux
    /// (un par tunnel actif), avec retrait automatique des ports en échec.
    /// </summary>
    public class SocksBalancer
    {
        private const string TAG = "SocksBalancer";
        public static int BalancerPort = 10900;
        private const int PIPE_BUFFER_SIZE = 131072;

        private TcpListener? _serverSocket;
        private volatile bool _running;
        private int _counter = 0;

        private volatile List<int> _activePorts;
        private volatile List<int> _healthyPorts;
        private readonly ConcurrentDictionary<int, int> _failCount = new();

        private int _totalConnections = 0;
        private int _successConnections = 0;
        private int _failedConnections = 0;
        private long _totalBytesTransferred = 0;

        public SocksBalancer(List<int> initialPorts)
        {
            _activePorts = new List<int>(initialPorts);
            _healthyPorts = new List<int>(initialPorts);
        }

        public long GetBytesTransferred() => Interlocked.Read(ref _totalBytesTransferred);
        public void ResetBytesTransferred() => Interlocked.Exchange(ref _totalBytesTransferred, 0);

        public void Start()
        {
            _running = true;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            BalancerPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            _serverSocket = listener;
            KighmuLogger.Info(TAG, "Balancer demarre");

            var thread = new Thread(() =>
            {
                while (_running)
                {
                    try
                    {
                        var client = listener.AcceptSocket();
                        Interlocked.Increment(ref _totalConnections);
                        int targetPort = NextPort();
                        ThreadPool.QueueUserWorkItem(_ => Relay(client, targetPort));
                    }
                    catch (Exception ex)
                    {
                        if (_running) KighmuLogger.Error(TAG, $"Accept error: {ex.Message}");
                    }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        public void UpdatePorts(List<int> newPorts)
        {
            if (newPorts.Count > 0)
            {
                _activePorts = new List<int>(newPorts);
                _healthyPorts = new List<int>(newPorts);
                _failCount.Clear();
                Interlocked.Exchange(ref _counter, 0);
            }
        }

        public void Stop()
        {
            _running = false;
            try { _serverSocket?.Stop(); } catch { /* ignore */ }
        }

        private int NextPort()
        {
            var current = _healthyPorts.Count > 0 ? _healthyPorts : _activePorts;
            if (current.Count == 0) return 10800;
            int idx = Interlocked.Increment(ref _counter) - 1;
            return current[idx % current.Count];
        }

        private void MarkPortFailed(int port)
        {
            int fails = _failCount.AddOrUpdate(port, 1, (_, v) => v + 1);
            if (fails >= 2)
            {
                var h = _healthyPorts.Where(p => p != port).ToList();
                if (h.Count > 0)
                {
                    _healthyPorts = h;
                    KighmuLogger.Warning(TAG, $"Port {port} retire echecs={fails} healthy=[{string.Join(",", h)}]");
                }
            }
        }

        private void MarkPortSuccess(int port)
        {
            _failCount[port] = 0;
            if (!_healthyPorts.Contains(port) && _activePorts.Contains(port))
                _healthyPorts = _healthyPorts.Append(port).Distinct().ToList();
        }

        private Socket ConnectToPort(int targetPort)
        {
            var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveBufferSize = PIPE_BUFFER_SIZE,
                SendBufferSize = PIPE_BUFFER_SIZE,
                NoDelay = true
            };
            var connectTask = server.ConnectAsync(IPAddress.Loopback, targetPort);
            if (!connectTask.Wait(5000))
                throw new TimeoutException("connect timeout");
            return server;
        }

        private void Relay(Socket client, int targetPort)
        {
            Socket? server = null;
            try
            {
                client.ReceiveBufferSize = PIPE_BUFFER_SIZE;
                client.SendBufferSize = PIPE_BUFFER_SIZE;
                client.NoDelay = true;

                var candidates = new List<int> { targetPort };
                candidates.AddRange(_activePorts.Where(p => p != targetPort));

                foreach (var port in candidates)
                {
                    try { server = ConnectToPort(port); break; }
                    catch { /* try next */ }
                }

                if (server == null)
                {
                    Interlocked.Increment(ref _failedConnections);
                    MarkPortFailed(targetPort);
                    try { client.Close(); } catch { /* ignore */ }
                    return;
                }

                Interlocked.Increment(ref _successConnections);
                MarkPortSuccess(targetPort);
                server.NoDelay = true;

                var serverCopy = server;
                ThreadPool.QueueUserWorkItem(_ => Pipe(client, serverCopy));
                Pipe(server, client);

                try { client.Close(); } catch { /* ignore */ }
                try { server.Close(); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                string msg = ex.Message ?? "";
                if (!msg.Contains("refused") && !msg.Contains("connect"))
                    KighmuLogger.Error(TAG, $"Relay error {targetPort}: {msg}");
                try { client.Close(); } catch { /* ignore */ }
            }
        }

        private void Pipe(Socket from, Socket to)
        {
            var buf = new byte[PIPE_BUFFER_SIZE];
            try
            {
                while (true)
                {
                    int n = from.Receive(buf);
                    if (n <= 0) break;
                    to.Send(buf, 0, n, SocketFlags.None);
                    Interlocked.Add(ref _totalBytesTransferred, n);
                }
            }
            catch { /* connexion fermée, normal */ }
        }
    }
}
