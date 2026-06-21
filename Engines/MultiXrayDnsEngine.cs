using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Equivalent de MultiXraySlowDnsEngine.kt.
    /// STEP 1 : tous les dnstt demarrent sequentiellement avec retry agressif.
    /// STEP 2 : tous les Xray demarrent en PARALLELE sur leurs dnstt respectifs.
    /// STEP 3 : balancer sur tous les ports SOCKS reussis.
    /// </summary>
    public class MultiXrayDnsEngine : ITunnelEngine
    {
        /// <summary>IP serveur a exclure des routes systeme (null = pas d'exclusion).</summary>
        public string? ServerIp => null;

        private const string TAG               = "MultiXrayDns";
        private const int    MAX_RETRIES       = 30;
        private const int    RETRY_DELAY_MS    = 800;
        private const int    DNSTT_TIMEOUT_MS  = 10000;
        private const int    XRAY_TIMEOUT_MS   = 8000;

        private class FluxConfig
        {
            public string         Label   { get; set; } = "";
            public XrayDnsProfile Profile { get; set; } = new XrayDnsProfile();
            public int            Index   { get; set; }
        }

        private readonly List<XrayDnsEngine> _xrayEngines  = new List<XrayDnsEngine>();
        private readonly List<SlowDnsEngine> _dnsttEngines = new List<SlowDnsEngine>();
        private readonly List<FluxConfig>    _fluxConfigs  = new List<FluxConfig>();
        private readonly object              _xrayLock     = new object();
        private readonly object              _dnsttLock    = new object();

        private SocksBalancer? _socksBalancer;
        private List<int>      _activePorts = new List<int>();
        private CancellationTokenSource? _cts;

        public async Task<int> Start()
        {
            _cts = new CancellationTokenSource();
            var repo     = new XrayDnsProfileRepository();
            var selected = repo.GetSelected();

            if (selected.Count == 0)
                throw new Exception("Aucun profil V2Ray+SlowDNS selectionne");

            // ── Nettoyage ────────────────────────────────────────────────────────
            KighmuLogger.Info(TAG, $"Nettoyage engines precedents ({_dnsttEngines.Count} dnstt, {_xrayEngines.Count} xray)...");
            lock (_xrayLock)
            {
                foreach (var e in _xrayEngines)
                    try { e.Stop().GetAwaiter().GetResult(); } catch { }
                _xrayEngines.Clear();
            }
            lock (_dnsttLock)
            {
                foreach (var e in _dnsttEngines)
                    try { e.Stop().GetAwaiter().GetResult(); } catch { }
                _dnsttEngines.Clear();
            }
            lock (_fluxConfigs) { _fluxConfigs.Clear(); }
            _socksBalancer?.Stop();
            _socksBalancer = null;
            await Task.Delay(500);

            // ── Construction des flux (tunnelCount par profil) ───────────────────
            var allFlux = new List<FluxConfig>();
            foreach (var profile in selected)
            {
                int count = Math.Max(1, Math.Min(profile.TunnelCount, 4));
                for (int fi = 0; fi < count; fi++)
                {
                    allFlux.Add(new FluxConfig
                    {
                        Label   = $"{profile.ProfileName}[{fi + 1}/{count}]",
                        Profile = profile,
                        Index   = allFlux.Count
                    });
                }
            }
            lock (_fluxConfigs) { _fluxConfigs.AddRange(allFlux); }

            int totalFlux = allFlux.Count;

            // ── STEP 1 : dnstt sequentiel ────────────────────────────────────────
            KighmuLogger.Info(TAG, $"=== STEP 1: {totalFlux} flux dnstt sequentiels ({selected.Count} profil(s)) ===");

            var dnsttResults = new List<(int idx, int port, FluxConfig flux, SlowDnsEngine engine)>();

            for (int i = 0; i < allFlux.Count; i++)
            {
                var flux    = allFlux[i];
                int port    = -1;
                int attempt = 0;

                // Construire un SlowDnsConfig depuis le profil XrayDns
                var dnsCfg = new Models.SlowDnsConfig
                {
                    DnsServer  = flux.Profile.DnsServer,
                    DnsPort    = flux.Profile.DnsPort,
                    Nameserver = flux.Profile.Nameserver,
                    PublicKey  = flux.Profile.PublicKey
                };

                var engine = new SlowDnsEngine(dnsCfg, "", "", flux.Index);

                KighmuLogger.Info(TAG, $"Flux[{i + 1}/{totalFlux}] dnstt demarrage: {flux.Label}");

                while (attempt < MAX_RETRIES && port <= 0)
                {
                    attempt++;
                    if (attempt > 1)
                    {
                        KighmuLogger.Warning(TAG, $"Flux[{i + 1}] dnstt retry {attempt}/{MAX_RETRIES} dans {RETRY_DELAY_MS}ms...");
                        engine.StopDnsttOnly();
                        await Task.Delay(RETRY_DELAY_MS);
                    }

                    try
                    {
                        var startTask = engine.StartDnsttOnly();
                        if (await Task.WhenAny(startTask, Task.Delay(DNSTT_TIMEOUT_MS)) == startTask)
                            port = await startTask;
                        else
                            port = -1;
                    }
                    catch (Exception ex)
                    {
                        KighmuLogger.Error(TAG, $"Flux[{i + 1}] dnstt exception tentative {attempt}: {ex.Message}");
                        port = -1;
                    }
                }

                if (port > 0)
                {
                    lock (_dnsttLock) { _dnsttEngines.Add(engine); }
                    dnsttResults.Add((i, port, flux, engine));
                    KighmuLogger.Info(TAG, $"Flux[{i + 1}] dnstt OK port={port} (tentative {attempt})");
                }
                else
                {
                    try { await engine.Stop(); } catch { }
                    KighmuLogger.Error(TAG, $"Flux[{i + 1}] dnstt ABANDON apres {MAX_RETRIES} tentatives");
                }
            }

            // ── STEP 2 : Xray en parallele ───────────────────────────────────────
            KighmuLogger.Info(TAG, $"=== STEP 2: {dnsttResults.Count}/{totalFlux} dnstt connectes - demarrage Xray en parallele ===");
            if (dnsttResults.Count == 0)
                throw new Exception("Aucun flux dnstt connecte apres tentatives");

            var xrayTasks = new List<Task<(XrayDnsEngine engine, int port)?>> ();
            foreach (var (idx, dnsttPort, flux, _) in dnsttResults)
            {
                var capturedIdx      = idx;
                var capturedPort     = dnsttPort;
                var capturedProfile  = flux.Profile;

                xrayTasks.Add(Task.Run(async () =>
                {
                    KighmuLogger.Info(TAG, $"XrayEngine[{capturedIdx}] demarrage dnsttPort={capturedPort}");
                    var xrayEngine = new XrayDnsEngine(capturedProfile, capturedIdx, capturedPort);
                    try
                    {
                        var startTask = xrayEngine.Start();
                        int socksPort = -1;
                        if (await Task.WhenAny(startTask, Task.Delay(XRAY_TIMEOUT_MS)) == startTask)
                            socksPort = await startTask;

                        if (socksPort > 0)
                        {
                            KighmuLogger.Info(TAG, $"XrayEngine[{capturedIdx}] CONNECTE socksPort={socksPort}");
                            return ((XrayDnsEngine engine, int port)?)(xrayEngine, socksPort);
                        }
                        else
                        {
                            KighmuLogger.Error(TAG, $"XrayEngine[{capturedIdx}] ECHEC port={socksPort}");
                            try { await xrayEngine.Stop(); } catch { }
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        KighmuLogger.Error(TAG, $"XrayEngine[{capturedIdx}] exception: {ex.Message}");
                        try { await xrayEngine.Stop(); } catch { }
                        return null;
                    }
                }));
            }

            await Task.WhenAll(xrayTasks);

            var xraySocksPorts = new List<int>();
            foreach (var task in xrayTasks)
            {
                var result = await task;
                if (result.HasValue)
                {
                    lock (_xrayLock) { _xrayEngines.Add(result.Value.engine); }
                    xraySocksPorts.Add(result.Value.port);
                }
            }

            if (xraySocksPorts.Count == 0)
                throw new Exception("Aucun XrayDnsEngine demarre avec succes");

            // ── STEP 3 : Balancer ────────────────────────────────────────────────
            _activePorts = xraySocksPorts;

            if (xraySocksPorts.Count > 1)
            {
                KighmuLogger.Info(TAG, $"=== STEP 3: SocksBalancer sur {xraySocksPorts.Count} ports ===");
                var balancer = new SocksBalancer(xraySocksPorts);
                balancer.Start();
                _socksBalancer = balancer;
                KighmuLogger.Info(TAG, $"Balancer actif port {SocksBalancer.BALANCER_PORT}");
            }

            int finalPort = xraySocksPorts.Count > 1 ? SocksBalancer.BALANCER_PORT : xraySocksPorts[0];
            KighmuLogger.Info(TAG, $"=== V2Ray+SlowDNS pret port={finalPort} {xraySocksPorts.Count} tunnel(s) ===");
            return finalPort;
        }

        public void StartTun2Socks(string tunAdapterName)
        {
            int targetPort = _activePorts.Count > 1 ? SocksBalancer.BALANCER_PORT
                           : _activePorts.Count > 0 ? _activePorts[0]
                           : throw new Exception("Aucun port actif");

            lock (_xrayLock)
            {
                if (_xrayEngines.Count > 0)
                    _xrayEngines[0].StartTun2SocksOnPort(tunAdapterName, targetPort);
            }
            KighmuLogger.Info(TAG, $"tun2socks V2Ray+SlowDNS port={targetPort}");
        }

        public async Task Stop()
        {
            KighmuLogger.Info(TAG, "Arret MultiXrayDnsEngine...");
            try { _cts?.Cancel(); } catch { }
            try { _socksBalancer?.Stop(); _socksBalancer = null; } catch { }
            lock (_xrayLock)
            {
                foreach (var e in _xrayEngines)
                    try { e.Stop().GetAwaiter().GetResult(); } catch { }
                _xrayEngines.Clear();
            }
            lock (_dnsttLock)
            {
                foreach (var e in _dnsttEngines)
                    try { e.Stop().GetAwaiter().GetResult(); } catch { }
                _dnsttEngines.Clear();
            }
            KighmuLogger.Info(TAG, "MultiXrayDnsEngine arrete");
        }

        public bool IsRunning()
        {
            lock (_xrayLock)
            {
                foreach (var e in _xrayEngines)
                    if (e.IsRunning()) return true;
                return false;
            }
        }
    }
}
