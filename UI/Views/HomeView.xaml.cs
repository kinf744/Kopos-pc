using KighmuVpnWindows.Models;
using KighmuVpnWindows.Vpn;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows.UI.Views
{
    public partial class HomeView : UserControl
    {
        private readonly KighmuVpnService _vpnService = KighmuVpnService.Instance;

        public HomeView()
        {
            InitializeComponent();
            _vpnService.StatusChanged += OnStatusChanged;
            _vpnService.ErrorOccurred += OnError;
            _vpnService.ActiveModeChanged += OnActiveModeChanged;
            _vpnService.TrafficUpdated += OnTrafficUpdated;
            PopulateProfileSelector();
            UpdateUI(_vpnService.Status);
            Unloaded += HomeView_Unloaded;
        }

        // Evite la fuite memoire : KighmuVpnService.Instance est un singleton qui
        // vit toute la session. Sans desabonnement, chaque navigation vers Home
        // empile un nouveau handler sur le meme service indefiniment.
        private void HomeView_Unloaded(object sender, RoutedEventArgs e)
        {
            _vpnService.StatusChanged -= OnStatusChanged;
            _vpnService.ErrorOccurred -= OnError;
            _vpnService.ActiveModeChanged -= OnActiveModeChanged;
            _vpnService.TrafficUpdated -= OnTrafficUpdated;
            Unloaded -= HomeView_Unloaded;
        }

        // ── Selecteur de profil ───────────────────────────────────────────────

        private void PopulateProfileSelector()
        {
            ProfileSelector.Items.Clear();
            foreach (TunnelMode mode in Enum.GetValues(typeof(TunnelMode)))
            {
                var item = new ComboBoxItem { Content = mode.Label(), Tag = mode };
                ProfileSelector.Items.Add(item);
                if (mode == _vpnService.ActiveMode)
                    ProfileSelector.SelectedItem = item;
            }
            ProfileSelector.SelectionChanged += ProfileSelector_SelectionChanged;
        }

        private async void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileSelector.SelectedItem is ComboBoxItem item && item.Tag is TunnelMode mode)
            {
                await _vpnService.SetMode(mode);
                UpdateUI(_vpnService.Status);
            }
        }

        // ── Bouton Connect ────────────────────────────────────────────────────

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false;
            try
            {
                await _vpnService.ToggleTunnel();
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        // ── Evenements service ────────────────────────────────────────────────

        private void OnStatusChanged(ConnectionStatus status)
        {
            Dispatcher.Invoke(() => UpdateUI(status));
        }

        private void OnActiveModeChanged(TunnelMode mode)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var obj in ProfileSelector.Items)
                {
                    if (obj is ComboBoxItem item && item.Tag is TunnelMode m && m == mode)
                    {
                        ProfileSelector.SelectionChanged -= ProfileSelector_SelectionChanged;
                        ProfileSelector.SelectedItem = item;
                        ProfileSelector.SelectionChanged += ProfileSelector_SelectionChanged;
                        break;
                    }
                }
                UpdateUI(_vpnService.Status);
            });
        }

        private void OnError(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusDetailText.Text = $"Erreur : {message}";
                MessageBox.Show(message, "Erreur tunnel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        // ── Trafic ──────────────────────────────────────────────────────────────

        private void OnTrafficUpdated(long rxBytes, long txBytes)
        {
            Dispatcher.Invoke(() =>
            {
                DownloadText.Text = FormatBytes(rxBytes);
                UploadText.Text   = FormatBytes(txBytes);
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)              return $"{bytes} B";
            if (bytes < 1024 * 1024)       return $"{bytes / 1024} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024)} MB";
            return $"{bytes / (1024 * 1024 * 1024)} GB";
        }

        // ── Mise a jour UI ────────────────────────────────────────────────────

        private void UpdateUI(ConnectionStatus status)
        {
            var green  = (Brush)FindResource("AccentGreenBrush");
            var grey   = (Brush)FindResource("TextSecondaryBrush");
            var orange = (Brush)TryFindResource("AccentOrangeBrush") ?? Brushes.Orange;
            var red    = (Brush)TryFindResource("AccentRedBrush")    ?? Brushes.Red;

            string modeLabel = _vpnService.ActiveMode.Label();

            switch (status)
            {
                case ConnectionStatus.CONNECTED:
                    StatusText.Text         = "CONNECTE";
                    StatusText.Foreground   = green;
                    StatusDetailText.Text   = $"Tunnel actif — {modeLabel}";
                    ConnectButton.IsEnabled = true;
                    break;

                case ConnectionStatus.CONNECTING:
                    StatusText.Text         = "CONNEXION...";
                    StatusText.Foreground   = orange;
                    StatusDetailText.Text   = $"Demarrage {modeLabel}...";
                    ConnectButton.IsEnabled = true;
                    break;

                case ConnectionStatus.STOPPING:
                    StatusText.Text         = "ARRET...";
                    StatusText.Foreground   = grey;
                    StatusDetailText.Text   = "Arret du tunnel...";
                    ConnectButton.IsEnabled = false;
                    break;

                case ConnectionStatus.ERROR:
                    StatusText.Text         = "ERREUR";
                    StatusText.Foreground   = red;
                    StatusDetailText.Text   = "Echec — verifiez la config";
                    ConnectButton.IsEnabled = true;
                    break;

                default:
                    StatusText.Text         = "DECONNECTE";
                    StatusText.Foreground   = grey;
                    StatusDetailText.Text   = "Appuyez pour vous connecter";
                    ConnectButton.IsEnabled = true;
                    break;
            }
        }
    }
}
