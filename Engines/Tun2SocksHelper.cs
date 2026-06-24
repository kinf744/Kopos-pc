using KighmuVpnWindows.Config;
using KighmuVpnWindows.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Helper commun pour lancer hev-socks5-tunnel.exe (remplace tun2socks).
    /// Genere le fichier YAML de config et demarre le processus.
    /// </summary>
    public static class Tun2SocksHelper
    {
        private const string TAG = "Tun2SocksHelper";
        private const string BINARY = "tun2socks.exe";

        private static string GetBinaryPath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "win", BINARY);

        private static string GetConfigPath(string instanceId) =>
            Path.Combine(LocalStorage.GetAppDataDir(), $"hev_config_{instanceId}.yaml");

        /// <summary>
        /// Demarre hev-socks5-tunnel vers le port SOCKS5 indique.
        /// Retourne le Process demarre ou null en cas d'erreur.
        /// </summary>
        public static Process? Start(string tunAdapterName, int socksPort, string instanceId = "main", bool udpEnabled = true, int mtu = 8500)
        {
            try
            {
                string binary = GetBinaryPath();
                if (!File.Exists(binary))
                {
                    KighmuLogger.Error(TAG, $"tun2socks.exe (hev-socks5-tunnel) introuvable: {binary}");
                    return null;
                }

                // Generer le fichier YAML
                string configPath = GetConfigPath(instanceId);
                string yaml = BuildYaml(tunAdapterName, socksPort, udpEnabled, mtu);
                File.WriteAllText(configPath, yaml);
                KighmuLogger.Info(TAG, $"Config YAML ecrite: {configPath}");

                var psi = new ProcessStartInfo
                {
                    FileName               = binary,
                    Arguments              = configPath,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) KighmuLogger.Info(TAG, e.Data); };
                process.ErrorDataReceived  += (s, e) => { if (e.Data != null) KighmuLogger.Info(TAG, e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                KighmuLogger.Info(TAG, $"hev-socks5-tunnel demarre port={socksPort} adapter={tunAdapterName} PID={process.Id}");
                KighmuLogger.Info(TAG, $"Contenu YAML envoye a hev-socks5-tunnel:\n{yaml}");

                // Verification differee : le process est-il toujours vivant apres 2s ?
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2000);
                    try
                    {
                        if (process.HasExited)
                            KighmuLogger.Error(TAG, $"hev-socks5-tunnel s'est arrete prematurement, exit code={process.ExitCode}");
                        else
                            KighmuLogger.Info(TAG, "hev-socks5-tunnel toujours actif apres 2s");
                    }
                    catch { }
                });

                return process;
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur demarrage hev-socks5-tunnel: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Demarre avec plusieurs ports SOCKS5 (mode multi-profil).
        /// Utilise le premier port comme port principal.
        /// </summary>
        public static Process? StartMulti(string tunAdapterName, System.Collections.Generic.List<int> socksPorts, string instanceId = "main")
        {
            if (socksPorts == null || socksPorts.Count == 0)
            {
                KighmuLogger.Error(TAG, "Aucun port SOCKS5 fourni pour StartMulti");
                return null;
            }
            // hev-socks5-tunnel ne supporte qu'un seul port SOCKS5
            // On utilise le SocksBalancer qui expose un seul port agregateur
            int balancerPort = SocksBalancer.BalancerPort > 0 ? SocksBalancer.BalancerPort : socksPorts[0];
            return Start(tunAdapterName, balancerPort, instanceId);
        }

        /// <summary>
        /// Arrete proprement le processus hev-socks5-tunnel.
        /// </summary>
        public static void Stop(Process? process, string instanceId = "main")
        {
            try
            {
                process?.Kill();
                KighmuLogger.Info(TAG, "hev-socks5-tunnel arrete");
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur arret hev-socks5-tunnel: {ex.Message}");
            }

            // Nettoyer le fichier YAML
            try
            {
                string configPath = GetConfigPath(instanceId);
                if (File.Exists(configPath)) File.Delete(configPath);
            }
            catch { }
        }

        private static string BuildYaml(string tunAdapterName, int socksPort, bool udpEnabled = true, int mtu = 8500)
        {
            string excludedRoutes = "";
            try
            {
                var gateway = GetDefaultGatewayForYaml();
                if (!string.IsNullOrEmpty(gateway))
                {
                    var dnsServers = DetectDnsServersForYaml();
                    foreach (var dns in dnsServers)
                        excludedRoutes += $"    - {dns}/32\n";

                    // Exclure la passerelle (peut servir de DNS)
                    excludedRoutes += $"    - {gateway}/32\n";

                    // Exclure le sous-reseau local (ex: 192.168.54.0/24)
                    var parts = gateway.Split('.');
                    if (parts.Length == 4)
                        excludedRoutes += $"    - {parts[0]}.{parts[1]}.{parts[2]}.0/24\n";
                }
            }
            catch { }

            return $@"tunnel:
  name: {tunAdapterName}
  mtu: {mtu}
  ipv4: 198.18.0.1
  ipv6: 'fc00::1'

socks5:
  port: {socksPort}
  address: 127.0.0.1
  udp: '{(udpEnabled ? "udp" : "disabled")}'

proxy:
  excluded-routes:
{excludedRoutes}misc:
  log-level: warn
";
        }

        private static string? GetDefaultGatewayForYaml()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "route",
                    Arguments = "print -4 0.0.0.0",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                foreach (var rawLine in output.Split('\n', '\r'))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("0.0.0.0"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3 && parts[2].Contains("."))
                            return parts[2];
                    }
                }
            }
            catch { }
            return null;
        }

        private static List<string> DetectDnsServersForYaml()
        {
            var servers = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "interface ipv4 show dns",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                foreach (var rawLine in output.Split('\n', '\r'))
                {
                    var line = rawLine.Trim();
                    if (!line.Contains("DNS") || !line.Contains(".")) continue;
                    var parts = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (part.Contains('.') && System.Net.IPAddress.TryParse(part, out _))
                        {
                            if (!servers.Contains(part))
                                servers.Add(part);
                        }
                    }
                }
            }
            catch { }
            // Fallback DNS connus
            if (servers.Count == 0)
                servers.AddRange(new[] { "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1" });
            return servers;
        }
    }
}
