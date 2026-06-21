using KighmuVpnWindows.Models;
using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Équivalent exact de MultiHysteriaEngine.kt.
    /// Connexion SEQUENTIELLE avec retry (20 tentatives max par profil).
    /// Tous les tunnels réussis sont équilibrés via SocksBalancer.
    /// Si aucun profil sélectionné -> fallback sur la config unique (HysteriaEngine).
    /// </summary>
    public class MultiHysteriaEngine : ITunnelEngine
    {
        /// <summary>IPs des serveurs Hysteria (un ou plusieurs profils), separees par virgule.</summary>
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

        private const string TAG = "MultiHysteria";
        private const int MAX_RETRIES = 20;
        private const int RETRY_DELAY_MS = 2000;
        private const int SESSION_TIMEOUT_MS = 35000;

        private readonly HysteriaConfig _baseConfig;
        private readonly List<HysteriaEngine> _engines = new();
        private readonly object _enginesLock = new();
        private SocksBalancer? _socksBalancer;
        private List<int> _activePorts = new();
        private Process? _tun2socksProcess;

        public MultiHysteriaEngine(HysteriaConfig baseConfig)
        {
            _baseConfig = baseConfig;
        }

        public async Task<int> Start()
        {
            var repo = new HysteriaProfileRepository();
            var selected = repo.GetSelected();

            // Fallback : aucun profil sélectionné -> engine unique
            if (selected.Count == 0)
            {
                KighmuLogger.Info(TAG, "Aucun profil Hysteria selectionne -> config par defaut");
                var engine = new HysteriaEngine(_baseConfig, HysteriaEngine.GetFreePort(), 0);
                lock (_enginesLock) { _engines.Add(engine); }
                int port = await engine.Start();
                _activePorts = new List<int> { port };
                return port;
            }

            // Nettoyage des engines précédents
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
            await Task.Delay(500);

            KighmuLogger.Info(TAG, $"=== STEP 1: Connexion SEQUENTIELLE {selected.Count} profil(s) Hysteria UDP ===");

            var successPorts = new List<int>();

            for (int idx = 0; idx < selected.Count; idx++)
            {
                var profile = selected[idx];
                KighmuLogger.Info(TAG, $"Profil[{idx + 1}/{selected.Count}] demarrage: {profile.ProfileName}");

                var cfg = new HysteriaConfig
                {
                    ServerAddress = profile.ServerAddress,
                    AuthPassword = profile.AuthPassword,
                    UploadMbps = profile.UploadMbps,
                    DownloadMbps = profile.DownloadMbps,
                    ObfsPassword = profile.ObfsPassword,
                    PortHopping = profile.PortHopping
                };

                int port = -1;
                int attempt = 0;

                while (attempt < MAX_RETRIES && port <= 0)
                {
                    attempt++;
                    KighmuLogger.Info(TAG, $"Profil[{idx + 1}] tentative {attempt}/{MAX_RETRIES}...");
                    int assignedPort = HysteriaEngine.GetFreePort();
                    KighmuLogger.Info(TAG, $"Profil[{idx + 1}] port SOCKS assigne: {assignedPort}");
                    var engine = new HysteriaEngine(cfg, assignedPort, idx);

                    try
                    {
                        var startTask = engine.Start();
                        var completed = await Task.WhenAny(startTask, Task.Delay(SESSION_TIMEOUT_MS));
                        port = completed == startTask ? await startTask : -1;

                        if (port > 0)
                        {
                            lock (_enginesLock) { _engines.Add(engine); }
                            KighmuLogger.Info(TAG, $"Profil[{idx + 1}] CONNECTE tentative={attempt} port={port}");
                        }
                        else
                        {
                            try { await engine.Stop(); } catch { /* ignore */ }
                            if (attempt < MAX_RETRIES)
                            {
                                KighmuLogger.Warning(TAG, $"Profil[{idx + 1}] echec tentative {attempt} - retry dans {RETRY_DELAY_MS}ms");
                                await Task.Delay(RETRY_DELAY_MS);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { await engine.Stop(); } catch { /* ignore */ }
                        KighmuLogger.Error(TAG, $"Profil[{idx + 1}] exception tentative {attempt}: {ex.Message}");
                        if (attempt < MAX_RETRIES) await Task.Delay(RETRY_DELAY_MS);
                    }
                }

                if (port > 0)
                    successPorts.Add(port);
                else
                    KighmuLogger.Error(TAG, $"Profil[{idx + 1}] ECHEC definitif apres {MAX_RETRIES} tentatives");
            }

            KighmuLogger.Info(TAG, $"=== STEP 2: {successPorts.Count}/{selected.Count} profils Hysteria connectes ===");

            if (successPorts.Count == 0)
                throw new Exception($"Aucun profil Hysteria connecte apres {MAX_RETRIES} tentatives chacun");

            _activePorts = successPorts;

            // Balancer si plusieurs tunnels réussis
            if (successPorts.Count > 1)
            {
                KighmuLogger.Info(TAG, $"=== STEP 3: Demarrage SocksBalancer sur {successPorts.Count} ports ===");
                var balancer = new SocksBalancer(successPorts);
                balancer.Start();
                _socksBalancer = balancer;
                KighmuLogger.Info(TAG, $"Balancer actif sur port {SocksBalancer.BalancerPort}");
            }

            int finalPort = successPorts.Count > 1 ? SocksBalancer.BalancerPort : successPorts[0];
            KighmuLogger.Info(TAG, $"=== Hysteria pret - port={finalPort}, {successPorts.Count} tunnel(s) actif(s) ===");
            return finalPort;
        }

        public void StartTun2Socks(string tunAdapterName)
        {
            int targetPort;
            if (_activePorts.Count > 1)
                targetPort = SocksBalancer.BalancerPort;
            else if (_activePorts.Count == 1)
                targetPort = _activePorts[0];
            else
            {
                KighmuLogger.Error(TAG, "Aucun port actif - impossible de demarrer tun2socks");
                return;
            }
            KighmuLogger.Info(TAG, $"tun2socks Hysteria -> port={targetPort} ({_activePorts.Count} tunnel(s))");
            _tun2socksProcess = Tun2SocksHelper.Start(tunAdapterName, targetPort, "hysteria_multi");
        }

        public async Task Stop()
        {
            KighmuLogger.Info(TAG, "Arret MultiHysteriaEngine...");
            Tun2SocksHelper.Stop(_tun2socksProcess, "hysteria_multi");
            _tun2socksProcess = null;
            try { _socksBalancer?.Stop(); _socksBalancer = null; } catch { /* ignore */ }

            List<HysteriaEngine> toStop;
            lock (_enginesLock)
            {
                toStop = new List<HysteriaEngine>(_engines);
                _engines.Clear();
            }
            foreach (var e in toStop)
            {
                try { await e.Stop(); } catch { /* ignore */ }
            }

            KighmuLogger.Info(TAG, "MultiHysteriaEngine arrete");
        }

        public bool IsRunning()
        {
            lock (_enginesLock)
            {
                return _engines.Any(e => e.IsRunning());
            }
        }
    }
}
