using KighmuVpnWindows.Utils;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace KighmuVpnWindows.Vpn
{
    /// <summary>
    /// Configure le routage systeme Windows pour forcer tout le trafic IPv4
    /// a transiter par l'adaptateur Wintun cree par hev-socks5-tunnel.
    /// Equivalent du VpnService.Builder.addRoute("0.0.0.0/0") cote Android,
    /// mais fait manuellement via route.exe / netsh.exe (necessite droits admin,
    /// deja requis via app.manifest).
    /// Technique "split-default" : 0.0.0.0/1 + 128.0.0.0/1 plutot que 0.0.0.0/0
    /// pour ne jamais toucher/ecraser la route par defaut existante.
    /// </summary>
    public static class RouteManager
    {
        private const string TAG = "RouteManager";

        /// <summary>
        /// Applique les routes systeme + DNS sur l'adaptateur tunnel.
        /// Attend que l'adaptateur soit pret (jusqu'a 5s) avant de configurer.
        /// </summary>
        private static List<string> _excludedServerIps = new List<string>();
        private static string? _cachedDefaultGateway;

        /// <summary>
        /// Ajoute les routes d'exclusion pour les IPs serveur AVANT de demarrer tun2socks.
        /// Appele avant StartTun2Socks pour eviter la boucle UDP (Hysteria, etc).
        /// </summary>
        public static void AddServerExclusions(string serverIp)
        {
            try
            {
                // Sauvegarder la passerelle AVANT toute modification des routes
                _cachedDefaultGateway = GetDefaultGateway();
                if (string.IsNullOrWhiteSpace(_cachedDefaultGateway))
                {
                    KighmuLogger.Warn(TAG, "Passerelle introuvable pour AddServerExclusions");
                    return;
                }
                string? originalGateway = _cachedDefaultGateway;
                var ips = serverIp.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct();
                foreach (var ip in ips)
                {
                    int physIdx = GetPhysicalAdapterIndex();
                    string ifPart = physIdx > 0 ? $" if {physIdx}" : "";
                    RunCommand("route", $"add {ip} mask 255.255.255.255 {originalGateway} metric 1{ifPart}");
                    if (!_excludedServerIps.Contains(ip))
                        _excludedServerIps.Add(ip);
                    KighmuLogger.Info(TAG, $"Pre-exclusion: {ip}/32 via {originalGateway} if={physIdx}");
                }
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"AddServerExclusions erreur: {ex.Message}");
            }
        }

        public static bool ApplyRoutes(string adapterName, string? serverIp = null, string tunnelLocalIp = "198.18.0.1", string dnsServer = "8.8.8.8")
        {
            try
            {
                int? idx = null;
                for (int i = 0; i < 10 && idx == null; i++)
                {
                    idx = GetAdapterIndex(adapterName);
                    if (idx == null) Thread.Sleep(500);
                }

                if (idx == null)
                {
                    KighmuLogger.Error(TAG, $"Adaptateur '{adapterName}' introuvable apres attente.");
                    return false;
                }

                KighmuLogger.Info(TAG, $"Adaptateur '{adapterName}' trouve, index={idx}");

                RunCommand("route", $"add 0.0.0.0 mask 128.0.0.0 {tunnelLocalIp} metric 1 if {idx}");
                RunCommand("route", $"add 128.0.0.0 mask 128.0.0.0 {tunnelLocalIp} metric 1 if {idx}");
                // SlowDNS: exclure les DNS du tunnel (SSH ne supporte pas UDP)
            // Utiliser la passerelle en cache (capturee avant toute modification des routes)
            string dnsGw = _cachedDefaultGateway ?? GetDefaultGateway();
            if (!string.IsNullOrWhiteSpace(dnsGw))
            {
                string[] dnsExcludes = { "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1" };
                foreach (var dnsIp in dnsExcludes)
                {
                    RunCommand("route", $"add {dnsIp} mask 255.255.255.255 {dnsGw} metric 1");
                    KighmuLogger.Info(TAG, $"DNS exclusion: {dnsIp}/32 via {dnsGw}");
                }
            }
            RunCommand("netsh", $"interface ip set dns name=\"{adapterName}\" static {dnsServer}");

                KighmuLogger.Info(TAG, "Routes systeme appliquees (tout le trafic -> tunnel, sauf IP serveur).");
                return true;
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur application routes: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Recupere l'IP de la passerelle par defaut active AVANT que le tunnel ne soit cree.
        /// Doit etre appele avant ApplyRoutes pour capturer la vraie passerelle Internet.
        /// </summary>
        /// <summary>Recupere l'index de l'interface physique (Ethernet ou WiFi) pour forcer la sortie des paquets serveur</summary>
        private static int GetPhysicalAdapterIndex()
        {
            try
            {
                // Methode fiable : lire la table de routage pour trouver
                // l'interface qui porte la route par defaut (0.0.0.0)
                // Elle est forcement connectee et active
                string output = RunCommandCapture("route", "print -4 0.0.0.0");
                foreach (var rawLine in output.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (!line.StartsWith("0.0.0.0")) continue;
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // Format: 0.0.0.0  0.0.0.0  <gateway>  <interface_ip>  <metric>
                    if (parts.Length < 4) continue;
                    string ifaceIp = parts[3];
                    // Trouver l'index de cette interface dans netsh
                    string netsh = RunCommandCapture("netsh", "interface ipv4 show interfaces");
                    foreach (var nLine in netsh.Split('\n'))
                    {
                        if (!nLine.Contains(ifaceIp) && !IsInterfaceMatchingIp(nLine.Trim(), ifaceIp)) continue;
                        var np = nLine.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (np.Length > 0 && int.TryParse(np[0], out int idx))
                        {
                            KighmuLogger.Info(TAG, $"Interface active detectee: index={idx} ip={ifaceIp}");
                            return idx;
                        }
                    }
                    // Fallback : chercher par IP via ipconfig
                    return GetAdapterIndexByIp(ifaceIp);
                }
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"GetPhysicalAdapterIndex erreur: {ex.Message}");
            }
            return 0;
        }

        private static bool IsInterfaceMatchingIp(string line, string ip) => false;

        private static int GetAdapterIndexByIp(string ip)
        {
            try
            {
                string output = RunCommandCapture("route", "print -4");
                foreach (var line in output.Split('\n'))
                {
                    var t = line.Trim();
                    if (!t.StartsWith("0.0.0.0")) continue;
                    var p = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 5 && p[3] == ip && int.TryParse(p[4], out int metric))
                    {
                        // Chercher l'index via netsh en matchant l'IP
                        string netsh = RunCommandCapture("netsh", $"interface ipv4 show addresses");
                        var blocks = netsh.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var block in blocks)
                        {
                            if (!block.Contains(ip)) continue;
                            // Extraire l'index depuis "Interface X"
                            foreach (var bl in block.Split('\n'))
                            {
                                if (!bl.Contains("idx") && !bl.Contains("Idx")) continue;
                                var bp = bl.Trim().Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var bv in bp)
                                    if (int.TryParse(bv, out int bidx))
                                        return bidx;
                            }
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        private static string? GetDefaultGateway()
        {
            try
            {
                string output = RunCommandCapture("route", "print -4 0.0.0.0");
                KighmuLogger.Info(TAG, $"route print output:\n{output}");
                foreach (var rawLine in output.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("0.0.0.0"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        // Format: 0.0.0.0  0.0.0.0  <gateway>  <interface>  <metric>
                        if (parts.Length >= 3 && parts[2].Contains("."))
                        {
                            KighmuLogger.Info(TAG, $"Passerelle detectee: {parts[2]}");
                            return parts[2];
                        }
                    }
                }
                KighmuLogger.Warn(TAG, "Aucune ligne 0.0.0.0 trouvee dans route print");
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur lecture passerelle par defaut: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Supprime les routes ajoutees par ApplyRoutes. A appeler au Stop().
        /// </summary>
        public static void RemoveRoutes(string tunnelLocalIp = "198.18.0.1")
        {
            try
            {
                RunCommand("route", $"delete 0.0.0.0 mask 128.0.0.0 {tunnelLocalIp}");
                RunCommand("route", $"delete 128.0.0.0 mask 128.0.0.0 {tunnelLocalIp}");

                foreach (var ip in _excludedServerIps)
                {
                    RunCommand("route", $"delete {ip} mask 255.255.255.255");
                    KighmuLogger.Info(TAG, $"Route exclusion supprimee: {ip}/32");
                }
                _excludedServerIps.Clear();
                _cachedDefaultGateway = null;

                // Nettoyer les exclusions DNS (SlowDNS)
                string[] dnsExcludes = { "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1" };
                foreach (var dnsIp in dnsExcludes)
                {
                    RunCommand("route", $"delete {dnsIp} mask 255.255.255.255");
                }

                KighmuLogger.Info(TAG, "Routes systeme supprimees.");
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur suppression routes: {ex.Message}");
            }
        }

        private static int? GetAdapterIndex(string adapterName)
        {
            string output = RunCommandCapture("netsh", "interface ipv4 show interfaces");
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Contains(adapterName))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[0], out int idx))
                        return idx;
                }
            }
            return null;
        }

        private static void RunCommand(string exe, string args)
        {
            KighmuLogger.Info(TAG, $"CMD: {exe} {args}");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            string stdout = p!.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            KighmuLogger.Info(TAG, $"CMD exit code: {p.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stdout)) KighmuLogger.Info(TAG, $"stdout: {stdout.Trim()}");
            if (!string.IsNullOrWhiteSpace(stderr)) KighmuLogger.Info(TAG, $"stderr: {stderr.Trim()}");
        }

        private static string RunCommandCapture(string exe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            string output = p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }
    }
}
