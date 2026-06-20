using KighmuVpnWindows.Config;
using KighmuVpnWindows.UI.Views;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows
{
    public partial class MainWindow : Window
    {
        private static readonly string CrashLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "KighmuVPN_crash.log"
        );

        private Button? _activeNavButton;

        public MainWindow()
        {
            Log("MainWindow() debut");
            try
            {
                Log("InitializeComponent...");
                InitializeComponent();
                Log("InitializeComponent OK");
                Log("NavigateTo HomeView...");
                NavigateTo(new HomeView(), NavHome);
                Log("NavigateTo HomeView OK");
            }
            catch (Exception ex)
            {
                Log($"CRASH MainWindow: {ex}");
                MessageBox.Show(ex.ToString(), "Crash MainWindow", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NavigateTo(UserControl view, Button activeBtn)
        {
            Log($"NavigateTo: {view.GetType().Name}");
            try
            {
                MainContent.Content = view;
                UpdateNavButtons(activeBtn);
                Log($"NavigateTo OK: {view.GetType().Name}");
            }
            catch (Exception ex)
            {
                Log($"CRASH NavigateTo: {ex}");
                MessageBox.Show(ex.ToString(), "Crash NavigateTo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateNavButtons(Button active)
        {
            try
            {
                var activeBrush   = (Brush)FindResource("NavActiveBrush");
                var inactiveBrush = (Brush)FindResource("NavInactiveBrush");

                foreach (var btn in new[] { NavHome, NavConfig, NavLogs, NavSettings })
                    btn.Foreground = (btn == active) ? activeBrush : inactiveBrush;

                _activeNavButton = active;
            }
            catch (Exception ex)
            {
                Log($"CRASH UpdateNavButtons: {ex}");
            }
        }

        private void NavHome_Click(object sender, RoutedEventArgs e)     => NavigateTo(new HomeView(),     NavHome);
        private void NavConfig_Click(object sender, RoutedEventArgs e)   => NavigateTo(new ConfigView(),   NavConfig);
        private void NavLogs_Click(object sender, RoutedEventArgs e)     => NavigateTo(new LogsView(),     NavLogs);
        private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(new SettingsView(), NavSettings);

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = !MenuPopup.IsOpen;
        }

        private void Menu_Import_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            var dlg = new OpenFileDialog
            {
                Filter = "Config KIGHMU (*.kighmu)|*.kighmu|JSON (*.json)|*.json|Tous|*.*",
                Title  = "Importer une configuration"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json    = File.ReadAllText(dlg.FileName);
                var manager = new ConfigManager();
                manager.ImportConfig(json);
                MessageBox.Show("Configuration importee avec succes !", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur import: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Menu_Export_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            var dlg = new SaveFileDialog
            {
                Filter   = "Config KIGHMU (*.kighmu)|*.kighmu",
                FileName = $"kighmu_config_{DateTime.Now:yyyyMMdd_HHmm}.kighmu",
                Title    = "Exporter la configuration"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var manager = new ConfigManager();
                var json    = manager.ExportConfig();
                File.WriteAllText(dlg.FileName, json);
                MessageBox.Show("Configuration exportee avec succes !", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur export: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Menu_Reset_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            var result = MessageBox.Show(
                "Reinitialiser toute la configuration ? Tous les profils seront supprimes.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                var manager = new ConfigManager();
                manager.ResetConfig();
                MessageBox.Show("Application reinitialisee.", "Reinitialisation",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Menu_License_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            var licenses = @"KIGHMU VPN pour Windows

Licences Open Source :

• MaterialDesignThemes (MIT License)
  https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit

• SSH.NET (MIT License)
  https://github.com/sshnet/SSH.NET

• Newtonsoft.Json (MIT License)
  https://www.newtonsoft.com/json

• Xray-core (MPL 2.0)
  https://github.com/XTLS/Xray-core

• Hysteria (MIT License)
  https://github.com/apernet/hysteria

• dnstt (MIT License)
  https://www.bamsoftware.com/software/dnstt/

• hev-socks5-tunnel (MIT License)
  https://github.com/heiher/hev-socks5-tunnel";

            MessageBox.Show(licenses, "Licences Open Source",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
