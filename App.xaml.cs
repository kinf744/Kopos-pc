using KighmuVpnWindows.Utils;
using System;
using System.IO;
using System.Reflection;
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

            // Extraire les ressources embarquées (binaires, DLLs) vers %LOCALAPPDATA%\KingOMVPN\bin\
            try
            {
                Log("Extraction des ressources...");
                ResourceManager.EnsureResources();
                Log("Ressources extraites avec succes.");
            }
            catch (Exception ex)
            {
                Log($"ERREUR extraction ressources: {ex.Message}");
                MessageBox.Show($"Echec extraction ressources: {ex.Message}", "Erreur fatale",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }

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
                if (ex.Exception?.InnerException != null)
                    Log($"[FirstChance Inner] {ex.Exception.InnerException.GetType().Name}: {ex.Exception.InnerException.Message}");
            };

            Log("Gestionnaires erreur installes");

            // Verifier les DLLs presentes
            Log("=== Verification DLLs ===");
            string dir = Environment.CurrentDirectory;
            foreach (var dll in new[] {
                "MaterialDesignThemes.Wpf.dll",
                "MaterialDesignColors.dll",
                "Newtonsoft.Json.dll",
                "Renci.SshNet.dll",
                "Microsoft.Xaml.Behaviors.dll"
            })
            {
                string path = Path.Combine(dir, dll);
                Log($"  {dll}: {(File.Exists(path) ? "PRESENT" : "MANQUANT")}");
            }

            // Verifier dossier bin/ dans AppData
            Log("=== Verification bin/ ===");
            string binDir = AppPaths.BinPath;
            if (Directory.Exists(binDir))
            {
                foreach (var f in Directory.GetFiles(binDir))
                    Log($"  bin/{Path.GetFileName(f)} ({new FileInfo(f).Length} octets)");
            }
            else
            {
                Log("  DOSSIER bin/ MANQUANT !");
            }

            try
            {
                Log("Appel base.OnStartup...");
                base.OnStartup(e);
                Log("base.OnStartup OK");
            }
            catch (Exception ex)
            {
                Log($"CRASH base.OnStartup: {ex}");
                MessageBox.Show(ex.ToString(), "Crash OnStartup", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Log("Chargement ressources App.xaml...");
                var brush = Resources["MaterialDesignPaper"];
                Log($"MaterialDesignPaper: {brush ?? "NULL"}");
            }
            catch (Exception ex)
            {
                Log($"CRASH ressources: {ex}");
            }

            try
            {
                Log("Creation MainWindow...");
                var win = new MainWindow();
                Log("MainWindow creee OK");
                win.Show();
                Log("MainWindow.Show() OK");
            }
            catch (Exception ex)
            {
                Log($"CRASH MainWindow: {ex}");
                MessageBox.Show(ex.ToString(), "Crash MainWindow", MessageBoxButton.OK, MessageBoxImage.Error);
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
