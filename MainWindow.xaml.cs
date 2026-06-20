using KighmuVpnWindows.UI.Views;
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
