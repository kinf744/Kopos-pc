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
        public static Process? Start(string tunAdapterName, int socksPort, string instanceId = "main")
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
                string yaml = BuildYaml(tunAdapterName, socksPort);
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
            return Start(tunAdapterName, socksPorts[0], instanceId);
        }

        /// <summary>
        /// Arrete proprement le processus hev-socks5-tunnel.
        /// </summary>
        public static void Stop(Process? process, string instanceId = "main")
        {
            try
            {
                process?.Kill(true);
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

        private static string BuildYaml(string tunAdapterName, int socksPort)
        {
            return $@"tunnel:
  name: {tunAdapterName}
  mtu: 8500
  ipv4: 198.18.0.1
  ipv6: 'fc00::1'

socks5:
  port: {socksPort}
  address: 127.0.0.1
  udp: 'udp'

misc:
  log-level: warn
";
        }
    }
}
