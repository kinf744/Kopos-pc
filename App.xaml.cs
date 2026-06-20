using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace KighmuVpnWindows
{
    public partial class App : Application
    {
        private static readonly string CrashLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "KighmuVPN_crash.log"
        );

        protected override void OnStartup(StartupEventArgs e)
        {
            Log("=== DEMARRAGE APPLICATION ===");
            Log($"OS: {Environment.OSVersion}");
            Log($"CLR: {Environment.Version}");
            Log($"Dir: {Environment.CurrentDirectory}");
            Log($"64bit: {Environment.Is64BitProcess}");

            // Capturer toutes les exceptions non gerees
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                string msg = ex.ExceptionObject?.ToString() ?? "Erreur inconnue";
                Log("=== UnhandledException ===");
                Log(msg);
                MessageBox.Show(msg, "Erreur fatale", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, ex) =>
            {
                string msg = ex.Exception?.ToString() ?? "Erreur inconnue";
                Log("=== DispatcherUnhandledException ===");
                Log(msg);
                MessageBox.Show(msg, "Erreur WPF", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.FirstChanceException += (s, ex) =>
            {
                Log($"[FirstChance] {ex.Exception?.GetType().Name}: {ex.Exception?.Message}");
            };

            Log("Gestionnaires erreur installes");

            try
            {
                Log("Appel base.OnStartup...");
                base.OnStartup(e);
                Log("base.OnStartup OK");
            }
            catch (Exception ex)
            {
                Log($"CRASH OnStartup: {ex}");
                MessageBox.Show(ex.ToString(), "Crash OnStartup", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Log("OnStartup termine");
        }

        private static void Log(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                File.AppendAllText(CrashLog, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
