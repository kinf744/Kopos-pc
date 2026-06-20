using KighmuVpnWindows.Utils;
using System;
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
        public static bool ApplyRoutes(string adapterName, string tunnelLocalIp = "198.18.0.1", string dnsServer = "1.1.1.1")
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
                RunCommand("netsh", $"interface ip set dns name=\"{adapterName}\" static {dnsServer}");

                KighmuLogger.Info(TAG, "Routes systeme appliquees (tout le trafic -> tunnel).");
                return true;
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Erreur application routes: {ex.Message}");
                return false;
            }
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
            if (!string.IsNullOrWhiteSpace(stdout)) KighmuLogger.Info(TAG, stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) KighmuLogger.Info(TAG, stderr.Trim());
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
