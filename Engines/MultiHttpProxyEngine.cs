using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Equivalent de MultiHttpProxyEngine.kt.
    /// Connexion sequentielle multi-profils + SocksBalancer si plusieurs tunnels.
    /// </summary>
    public class MultiHttpProxyEngine : ITunnelEngine
    {
        /// <summary>IPs des serveurs proxy (un ou plusieurs profils), separees par virgule.</summary>
        public string? ServerIp
        {
            get
            {
                lock (_enginesLock)
                {
                    var ips = _engines.Select(e => e.ServerIp)
                        .Where(ip => !string.IsNullOrWhiteSpace(ip))
                        .Distinct()
                        .ToList();
                    return ips.Count > 0 ? string.Join(",", ips) : null;
                }
            }
        }

        private const string TAG              = "MultiHttpProxy";
        private const int    MAX_RETRIES      = 5;
        private const int    RETRY_DELAY_MS   = 1500;
        private const int    SESSION_TIMEOUT_MS = 75000;

        private readonly List<HttpProxyEngine> _engines      = new List<HttpProxyEngine>();
        private readonly object                _enginesLock  = new object();
        private SocksBalancer?  _socksBalancer;
        private List<int>       _activePorts  = new List<int>();
        private CancellationTokenSource? _cts;
        private Process? _tun2socksProcess;

        private static string GetBinaryPath(string name) =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "win", name);

        public async Task<int> Start()
        {
            _cts = new CancellationTokenSource();
            var repo     = new HttpProxyProfileRepository();
            var selected = repo.GetSelected();

            // ── Nettoyage engines precedents ────────────────────────────────────
            KighmuLogger.Info(TAG, $"Nettoyage engines precedents ({_engines.Count})...");
            lock (_enginesLock)
            {
                foreach (var e in _engines)
                    try { e.Stop().GetAwaiter().GetResult(); } catch { }
                _engines.Clear();
            }
            _socksBalancer?.Stop();
            _socksBalancer = null;
            await Task.Delay(300);

            // ── Aucun profil selectionne : config par defaut impossible sur Windows
            //    (pas de KighmuConfig ici) → on leve une exception claire
            if (selected.Count == 0)
                throw new Exception("Aucun profil HTTP Proxy selectionne");

            // ── STEP 1 : Connexion sequentielle ──────────────────────────────────
            KighmuLogger.Info(TAG, $"=== STEP 1: Connexion SEQUENTIELLE {selected.Count} profil(s) ===");
            var successPorts = new List<int>();

            for (int idx = 0; idx < selected.Count; idx++)
            {
                var profile = selected[idx];
                KighmuLogger.Info(TAG, $"Profil[{idx + 1}/{selected.Count}] demarrage: {profile.ProfileName}");

                int  port    = -1;
                int  attempt = 0;

                while (attempt < MAX_RETRIES && port <= 0)
                {
                    attempt++;
                    KighmuLogger.Info(TAG, $"Profil[{idx + 1}] tentative {attempt}/{MAX_RETRIES}...");

                    var engine = new HttpProxyEngine(
                        proxyHost     : profile.ProxyHost,
                        proxyPort     : profile.ProxyPort,
                        customPayload : profile.CustomPayload,
                        sshHost       : profile.SshHost,
                        sshPort       : profile.SshPort,
                        sshUser       : profile.SshUser,
                        sshPass       : profile.SshPass,
                        profileIndex  : idx
                    );

                    try
                    {
                        var startTask = engine.Start();
                        if (await Task.WhenAny(startTask, Task.Delay(SESSION_TIMEOUT_MS)) == startTask)
                            port = await startTask;
                        else
                            port = -1;

                        if (port > 0)
                        {
                            lock (_enginesLock) { _engines.Add(engine); }
                            KighmuLogger.Info(TAG, $"Profil[{idx + 1}] CONNECTE port={port}");
                        }
                        else
                        {
                            try { await engine.Stop(); } catch { }
                            if (attempt < MAX_RETRIES)
                            {
                                KighmuLogger.Warning(TAG, $"Profil[{idx + 1}] echec {attempt} — retry {RETRY_DELAY_MS}ms");
                                await Task.Delay(RETRY_DELAY_MS);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { await engine.Stop(); } catch { }
                        KighmuLogger.Error(TAG, $"Profil[{idx + 1}] exception {attempt}: {ex.Message}");
                        if (attempt < MAX_RETRIES) await Task.Delay(RETRY_DELAY_MS);
                    }
                }

                if (port > 0) successPorts.Add(port);
                else KighmuLogger.Error(TAG, $"Profil[{idx + 1}] ECHEC definitif");
            }

            // ── STEP 2 : Bilan ───────────────────────────────────────────────────
            KighmuLogger.Info(TAG, $"=== STEP 2: {successPorts.Count}/{selected.Count} connectes ===");
            if (successPorts.Count == 0)
                throw new Exception("Aucun profil HTTP Proxy connecte");

            _activePorts = successPorts;

            // ── STEP 3 : Balancer si plusieurs tunnels ───────────────────────────
            if (successPorts.Count > 1)
            {
                KighmuLogger.Info(TAG, $"=== STEP 3: SocksBalancer sur {successPorts.Count} ports ===");
                var balancer = new SocksBalancer(successPorts);
                balancer.Start();
                _socksBalancer = balancer;
                KighmuLogger.Info(TAG, $"Balancer actif port {SocksBalancer.BalancerPort}");
            }

            int finalPort = successPorts.Count > 1 ? SocksBalancer.BalancerPort : successPorts[0];
            KighmuLogger.Info(TAG, $"=== HTTP Proxy pret port={finalPort} {successPorts.Count} tunnel(s) ===");
            return finalPort;
        }

        public void StartTun2Socks(string tunAdapterName)
        {
            int targetPort = _activePorts.Count > 1 ? SocksBalancer.BalancerPort
                           : _activePorts.Count > 0 ? _activePorts[0]
                           : throw new Exception("Aucun port actif");

            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, "httpproxy_multi");
            KighmuLogger.Info(TAG, $"tun2socks HTTP Proxy port={targetPort}");
        }

        public async Task Stop()
        {
            KighmuLogger.Info(TAG, "Arret MultiHttpProxyEngine...");
            try { _cts?.Cancel(); } catch { }
            try { _socksBalancer?.Stop(); _socksBalancer = null; } catch { }
            Tun2SocksHelper.Stop(_tun2socksProcess, "httpproxy_multi");
            _tun2socksProcess = null;
            List<HttpProxyEngine> snapshot;
            lock (_enginesLock)
            {
                snapshot = new List<HttpProxyEngine>(_engines);
                _engines.Clear();
            }
            var stopTask = Task.Run(() =>
            {
                foreach (var e in snapshot)
                    try { e.Stop().GetAwaiter().GetResult(); } catch { }
            });
            await Task.WhenAny(stopTask, Task.Delay(3000));
            KighmuLogger.Info(TAG, "MultiHttpProxyEngine arrete");
        }

        public bool IsRunning()
        {
            lock (_enginesLock)
            {
                foreach (var e in _engines)
                    if (e.IsRunning()) return true;
                return false;
            }
        }
    }
}
