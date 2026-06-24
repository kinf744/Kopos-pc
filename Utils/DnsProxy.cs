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
            int port = ((IPEndPoint)_server.Client.LocalEndPoint).Port;
            KighmuLogger.Info("DnsProxy", $"DnsProxy: 127.0.0.1:{port} -> {_upstream} (bind={_bindAddress})");
            SlowDnsLogger.Info("DnsProxy", $"DnsProxy demarre: 127.0.0.1:{port} -> {_upstream} (bind={_bindAddress})");
        }

        private async Task ReceiveLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _server.ReceiveAsync();
                    var query = result.Buffer;
                    var client = result.RemoteEndPoint;
                    if (query.Length < 12) continue;
                    ushort txId = (ushort)((query[0] << 8) | query[1]);
                    string domain = ExtractDnsDomain(query);
                    SlowDnsLogger.Info("DnsProxy", $"REQ  TX={txId} client={client} domaine={domain} taille={query.Length}");
                    _pending[txId] = client;
                    _ = ForwardAsync(query, txId);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    KighmuLogger.Warning("DnsProxy", $"Receive: {ex.Message}");
                    SlowDnsLogger.Warn("DnsProxy", $"Receive error: {ex.Message}");
                }
            }
        }

        private async Task ForwardAsync(byte[] query, ushort txId)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var upstream = new UdpClient();
                upstream.Client.Bind(new IPEndPoint(_bindAddress, 0));
                await upstream.SendAsync(query, query.Length, _upstream);
                SlowDnsLogger.Info("DnsProxy", $"UP   TX={txId} envoye a {_upstream}");

                var recvTask = upstream.ReceiveAsync();
                if (await Task.WhenAny(recvTask, Task.Delay(5000)) == recvTask)
                {
                    var response = recvTask.Result;
                    if (_pending.TryRemove(txId, out var client))
                    {
                        await _server.SendAsync(response.Buffer, response.Buffer.Length, client);
                        sw.Stop();
                        SlowDnsLogger.Info("DnsProxy", $"RESP TX={txId} vers client={client} taille={response.Buffer.Length} temps={sw.ElapsedMilliseconds}ms");
                    }
                }
                else
                {
                    SlowDnsLogger.Warn("DnsProxy", $"FAIL TX={txId} timeout 5s upstream {_upstream}");
                    _pending.TryRemove(txId, out _);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                KighmuLogger.Warning("DnsProxy", $"Forward[{txId}]: {ex.Message}");
                SlowDnsLogger.Warn("DnsProxy", $"FAIL TX={txId} temps={sw.ElapsedMilliseconds}ms erreur={ex.Message}");
                _pending.TryRemove(txId, out _);
            }
        }

        public int ListenPort => ((IPEndPoint)_server.Client.LocalEndPoint).Port;

        private static string ExtractDnsDomain(byte[] query)
        {
            try
            {
                if (query.Length < 12) return "(trop court)";
                // Sauter l'entete DNS de 12 octets, decoder les labels
                int pos = 12;
                var labels = new System.Collections.Generic.List<string>();
                while (pos < query.Length)
                {
                    int len = query[pos];
                    if (len == 0) break;
                    if ((len & 0xC0) == 0xC0) { labels.Add("(pointeur)"); break; } // compression
                    if (pos + 1 + len > query.Length) break;
                    pos++;
                    labels.Add(System.Text.Encoding.ASCII.GetString(query, pos, len));
                    pos += len;
                }
                string domain = string.Join(".", labels);
                // Type de requete (QTYPE aux octets pos+1, pos+2)
                ushort qtype = pos + 3 < query.Length ? (ushort)((query[pos + 1] << 8) | query[pos + 2]) : (ushort)0;
                string typeStr = qtype == 1 ? "A" : qtype == 28 ? "AAAA" : qtype == 15 ? "MX" : qtype.ToString();
                return string.IsNullOrEmpty(domain) ? "(vide)" : $"{domain} ({typeStr})";
            }
            catch { return "(erreur parse)"; }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            try { _server?.Close(); } catch { }
            SlowDnsLogger.Info("DnsProxy", "DnsProxy stoppe");
        }
    }
}
