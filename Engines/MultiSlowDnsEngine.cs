using KighmuVpnWindows.Models;
using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Équivalent exact de MultiSlowDnsEngine.kt.
    /// Connexion séquentielle avec retry agressif (30 tentatives, 300ms) par flux.
    /// Warm replacement : nouveau tunnel démarré AVANT de tuer l'ancien en cas de dégradation/mort.
    /// </summary>
    public class MultiSlowDnsEngine : ITunnelEngine
    {
        /// <summary>IP serveur a exclure des routes systeme (null = pas d'exclusion).</summary>
        public string? ServerIp => null;

        private const string TAG = "MultiSlowDnsEngine";
        private const int SESSION_TIMEOUT_MS = 15000; // kex SSH via dnstt peut prendre 8-12s

        private readonly SlowDnsConfig _baseConfig;
        private readonly string _baseSshUser;
        private readonly string _baseSshPass;

        private readonly List<SlowDnsEngine> _engines = new();
        private readonly object _enginesLock = new();
        private int _activePort = 10800;
        private SocksBalancer? _socksBalancer;
        private volatile int _replacingCount = 0;
        private CancellationTokenSource? _monitorCts;

        public MultiSlowDnsEngine(SlowDnsConfig baseConfig, string baseSshUser, string baseSshPass)
        {
            _baseConfig = baseConfig;
            _baseSshUser = baseSshUser;
            _baseSshPass = baseSshPass;
        }

        public async Task<int> Start()
        {
            var repo = new SlowDnsProfileRepository();
            var selected = repo.GetSelected();

            if (selected.Count == 0)
            {
                KighmuLogger.Info(TAG, "Aucun profil sélectionné → config par défaut");
                var engine = new SlowDnsEngine(_baseConfig, _baseSshUser, _baseSshPass, 0);
                lock (_enginesLock) { _engines.Add(engine); }
                return await engine.Start();
            }

            // Nettoyer les engines précédents avant de relancer
            KighmuLogger.Info(TAG, $"Nettoyage engines precedents ({_engines.Count})...");
            lock (_enginesLock)
            {
                foreach (var e in _engines)
                {
                    try { e.Stop().Wait(); } catch { /* ignore */ }
                }
                _engines.Clear();
            }
            _socksBalancer?.Stop();
            _socksBalancer = null;

            // Attendre la libération des ressources noyau
            await Task.Delay(500);

            // Calcul du total de flux (profils x tunnelCount chacun)
            int totalFlux = selected.Sum(p => Math.Max(1, Math.Min(4, p.TunnelCount)));
            KighmuLogger.Info(TAG, $"=== STEP 1: Connexion sequentielle {totalFlux} flux ({selected.Count} profil(s)) ===");

            const int MAX_RETRIES = 30;
            const int RETRY_DELAY_MS = 300;

            var results = new List<(int idx, int port)>();
            int globalIdx = 0;

            foreach (var profile in selected)
            {
                int count = Math.Max(1, Math.Min(4, profile.TunnelCount));
                KighmuLogger.Info(TAG, $"Profil '{profile.ProfileName}' -> {count} flux paralleles");

                for (int fluxIdx = 0; fluxIdx < count; fluxIdx++)
                {
                    string sessionLabel = $"{profile.ProfileName}[{fluxIdx + 1}/{count}]";
                    KighmuLogger.Info(TAG, $"Session[{globalIdx + 1}/{totalFlux}] demarrage: {sessionLabel}");

                    int port = -1;
                    int attempt = 0;
                    var cfg = BuildConfig(profile);
                    var activeEngine = new SlowDnsEngine(cfg, profile.SshUser, profile.SshPass, globalIdx);
                    lock (_enginesLock) { _engines.Add(activeEngine); }

                    while (attempt < MAX_RETRIES && port <= 0)
                    {
                        attempt++;
                        if (attempt > 1)
                        {
                            KighmuLogger.Warning(TAG, $"Session[{globalIdx + 1}] retry {attempt}/{MAX_RETRIES} dans {RETRY_DELAY_MS}ms...");
                            // Tuer seulement SSH, garder dnstt vivant pour retry rapide
                            try { activeEngine.StopSshOnly(); } catch { /* ignore */ }
                            await Task.Delay(RETRY_DELAY_MS);
                        }

                        try
                        {
                            KighmuLogger.Info(TAG, $"Session[{globalIdx + 1}] tentative {attempt}/{MAX_RETRIES}: {sessionLabel}");
                            var startTask = activeEngine.Start();
                            var completed = await Task.WhenAny(startTask, Task.Delay(SESSION_TIMEOUT_MS));
                            port = completed == startTask ? await startTask : -1;
                        }
                        catch (Exception ex)
                        {
                            KighmuLogger.Error(TAG, $"Session[{globalIdx + 1}] tentative {attempt} FAILED: {ex.Message}");
                            port = -1;
                        }
                    }

                    if (port > 0)
                        KighmuLogger.Info(TAG, $"Session[{globalIdx + 1}] CONNECTEE OK port={port} (tentative {attempt}) flux={sessionLabel}");
                    else
                        KighmuLogger.Error(TAG, $"Session[{globalIdx + 1}] ABANDON apres {MAX_RETRIES} tentatives flux={sessionLabel}");

                    results.Add((globalIdx, port));
                    globalIdx++;
                }
            }

            // STEP 2 : Résultats
            var successPorts = results.Where(r => r.port > 0).Select(r => r.port).ToList();
            int failedCount = results.Count(r => r.port <= 0);
            if (failedCount > 0) KighmuLogger.Warning(TAG, $"{failedCount} session(s) ont echoue");

            if (successPorts.Count == 0)
                throw new Exception($"Aucune session SlowDNS connectee sur {selected.Count} tentatives");

            // STEP 3 : Démarrer le balancer sur tous les ports SOCKS connectés
            var connectedPorts = successPorts.Count > 0 ? successPorts : new List<int> { SlowDnsEngine.BASE_SOCKS_PORT };
            KighmuLogger.Info(TAG, $"Ports SOCKS actifs: [{string.Join(",", connectedPorts)}]");

            // Diagnostic : vérifier chaque port SOCKS avant de démarrer le balancer
            foreach (var port in connectedPorts)
            {
                try
                {
                    var sock = new TcpClient();
                    var connectTask = sock.ConnectAsync(IPAddress.Loopback, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(1000)) != connectTask || !sock.Connected)
                    {
                        KighmuLogger.Warning(TAG, $"Port {port}: TCP non accessible");
                        continue;
                    }
                    var stream = sock.GetStream();
                    await stream.WriteAsync(new byte[] { 5, 1, 0 }, 0, 3);
                    var buf = new byte[2];
                    int read = await stream.ReadAsync(buf, 0, 2);
                    if (read == 2 && buf[0] == 5)
                        KighmuLogger.Info(TAG, $"Port {port}: SOCKS5 OK (reponse: {buf[0]},{buf[1]})");
                    else
                        KighmuLogger.Warning(TAG, $"Port {port}: TCP OK mais SOCKS5 invalide (lu={read})");
                }
                catch (Exception ex)
                {
                    KighmuLogger.Error(TAG, $"Port {port}: INACCESSIBLE ({ex.Message})");
                }
            }

            var balancer = new SocksBalancer(connectedPorts);
            balancer.Start();
            _socksBalancer = balancer;
            _activePort = SocksBalancer.BalancerPort;

            await Task.Delay(200);
            try
            {
                var sock = new TcpClient();
                var connectTask = sock.ConnectAsync(IPAddress.Loopback, _activePort);
                if (await Task.WhenAny(connectTask, Task.Delay(1000)) != connectTask || !sock.Connected)
                    KighmuLogger.Error(TAG, $"Balancer port {_activePort}: INACCESSIBLE");
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Balancer port {_activePort}: INACCESSIBLE ({ex.Message})");
            }

            KighmuLogger.Info(TAG, $"VPN pret: {successPorts.Count} tunnels actifs port={_activePort}");

            // Surveiller les sessions en arrière-plan
            _monitorCts = new CancellationTokenSource();
            _ = MonitorSessions(selected, _monitorCts.Token);

            return _activePort;
        }

        private async Task MonitorSessions(List<SlowDnsProfile> profiles, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(5000, token).ContinueWith(_ => { });
                if (token.IsCancellationRequested) break;

                List<SlowDnsEngine> snapshot;
                lock (_enginesLock) { snapshot = new List<SlowDnsEngine>(_engines); }

                int alive = snapshot.Count(e => e.IsRunning());
                int total = snapshot.Count;

                for (int idx = 0; idx < snapshot.Count; idx++)
                {
                    var engine = snapshot[idx];
                    bool degraded = engine.IsDegraded && !token.IsCancellationRequested;
                    bool dead = !engine.IsRunning() && !token.IsCancellationRequested;

                    if ((dead || degraded) && !token.IsCancellationRequested)
                    {
                        if (degraded && !dead)
                            KighmuLogger.Warning(TAG, $"Session[{idx}] degradee - reconnexion preventive...");
                        else
                            KighmuLogger.Warning(TAG, $"Session[{idx}] morte - warm replacement...");

                        int capturedIdx = idx;
                        var capturedEngine = engine;
                        _ = Task.Run(async () =>
                        {
                            Interlocked.Increment(ref _replacingCount);
                            try
                            {
                                if (capturedIdx >= profiles.Count) return;
                                var profile = profiles[capturedIdx];

                                var newEngine = new SlowDnsEngine(BuildConfig(profile), profile.SshUser, profile.SshPass, capturedIdx);
                                var startTask = newEngine.Start();
                                var completed = await Task.WhenAny(startTask, Task.Delay(SESSION_TIMEOUT_MS * 5));
                                int port = completed == startTask ? await startTask : -1;

                                if (port > 0)
                                {
                                    lock (_enginesLock) { _engines[capturedIdx] = newEngine; }
                                    List<int> alivePorts;
                                    lock (_enginesLock)
                                    {
                                        alivePorts = _engines.Where(e => e.IsRunning())
                                            .Select(e => e.GetSocksPort())
                                            .Where(p => p.HasValue)
                                            .Select(p => p!.Value)
                                            .ToList();
                                    }
                                    if (alivePorts.Count > 0) _socksBalancer?.UpdatePorts(alivePorts);
                                    KighmuLogger.Info(TAG, $"Session[{capturedIdx}] warm replacement OK port={port}");
                                    capturedEngine.IsDegraded = false;
                                    try { await capturedEngine.Stop(); } catch { /* ignore */ }
                                }
                                else
                                {
                                    KighmuLogger.Error(TAG, $"Session[{capturedIdx}] warm replacement echoue - conservation ancien tunnel");
                                    try { await newEngine.Stop(); } catch { /* ignore */ }
                                    try { await capturedEngine.Stop(); } catch { /* ignore */ }
                                }
                            }
                            catch (Exception ex)
                            {
                                KighmuLogger.Error(TAG, $"Session[{capturedIdx}] erreur warm replacement: {ex.Message}");
                            }
                            finally
                            {
                                Interlocked.Decrement(ref _replacingCount);
                            }
                        }, token);
                    }
                }

                // Si toutes les sessions sont mortes -> redémarrage complet
                if (alive == 0 && total > 0 && _replacingCount == 0)
                {
                    KighmuLogger.Error(TAG, "Toutes les sessions tombees - redemarrage complet...");
                    List<SlowDnsEngine> toStop;
                    lock (_enginesLock) { toStop = new List<SlowDnsEngine>(_engines); _engines.Clear(); }
                    foreach (var e in toStop) { try { await e.Stop(); } catch { /* ignore */ } }

                    await Task.Delay(1000); // laisser le systeme liberer les sockets UDP
                    try { await Start(); } catch (Exception ex) { KighmuLogger.Error(TAG, $"Echec redemarrage complet: {ex.Message}"); }
                    break;
                }
            }
        }

        public void StartTun2Socks(string tunAdapterName)
        {
            int balancerPort = SocksBalancer.BalancerPort;
            try
            {
                List<SlowDnsEngine> running;
                lock (_enginesLock) { running = _engines.Where(e => e.IsRunning()).ToList(); }
                var firstEngine = running.FirstOrDefault() ?? _engines.FirstOrDefault();

                if (firstEngine != null)
                {
                    KighmuLogger.Info(TAG, $"tun2socks SlowDNS -> port={balancerPort} adapter={tunAdapterName}");
                    firstEngine.StartTun2SocksOnPort(tunAdapterName, balancerPort);
                }
                else
                {
                    KighmuLogger.Error(TAG, "Aucune session disponible pour tun2socks!");
                }
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"StartTun2Socks erreur: {ex.Message}");
            }
        }

        public async Task Stop()
        {
            KighmuLogger.Info(TAG, $"Arret de {_engines.Count} session(s)...");

            try { _socksBalancer?.Stop(); _socksBalancer = null; } catch { /* ignore */ }
            try { _monitorCts?.Cancel(); } catch { /* ignore */ }

            List<SlowDnsEngine> toStop;
            lock (_enginesLock) { toStop = new List<SlowDnsEngine>(_engines); _engines.Clear(); }
            foreach (var engine in toStop)
            {
                try { await engine.Stop(); } catch { /* ignore */ }
            }

            KighmuLogger.Info(TAG, "Toutes les ressources MultiSlowDNS liberees");
        }

        public bool IsRunning()
        {
            lock (_enginesLock) { return _engines.Any(e => e.IsRunning()); }
        }

        private SlowDnsConfig BuildConfig(SlowDnsProfile p)
        {
            var cfg = _baseConfig.Clone();
            cfg.SshHost = p.SshHost;
            cfg.SshPort = p.SshPort;
            cfg.SshUser = p.SshUser;
            cfg.SshPass = p.SshPass;
            cfg.DnsServer = p.DnsServer;
            cfg.Nameserver = p.Nameserver;
            cfg.PublicKey = (p.PublicKey ?? "").Trim().Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
            return cfg;
        }
    }
}
