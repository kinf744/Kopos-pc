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
            // Abonnement aux evenements du service
            _vpnService.StatusChanged  += OnStatusChanged;
            _vpnService.ErrorOccurred  += OnError;
            // Afficher l'etat courant au chargement
            UpdateUI(_vpnService.Status);
        }

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

        private void OnStatusChanged(ConnectionStatus status)
        {
            // Retour sur le thread UI
            Dispatcher.Invoke(() => UpdateUI(status));
        }

        private void OnError(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusDetailText.Text = $"Erreur : {message}";
                MessageBox.Show(message, "Erreur tunnel", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void UpdateUI(ConnectionStatus status)
        {
            var green = (Brush)FindResource("AccentGreenBrush");
            var grey  = (Brush)FindResource("TextSecondaryBrush");
            var red   = (Brush)TryFindResource("AccentRedBrush") ?? Brushes.Red;

            switch (status)
            {
                case ConnectionStatus.CONNECTED:
                    StatusText.Text        = "CONNECTE";
                    StatusText.Foreground  = green;
                    StatusDetailText.Text  = $"Tunnel actif — {_vpnService.ActiveMode.Label()}";
                    ConnectButton.Content  = "DISCONNECT";
                    ConnectButton.IsEnabled = true;
                    break;

                case ConnectionStatus.CONNECTING:
                    StatusText.Text        = "CONNEXION...";
                    StatusText.Foreground  = grey;
                    StatusDetailText.Text  = "Demarrage du tunnel...";
                    ConnectButton.Content  = "ANNULER";
                    ConnectButton.IsEnabled = true;
                    break;

                case ConnectionStatus.STOPPING:
                    StatusText.Text        = "ARRET...";
                    StatusText.Foreground  = grey;
                    StatusDetailText.Text  = "Arret du tunnel...";
                    ConnectButton.IsEnabled = false;
                    break;

                case ConnectionStatus.ERROR:
                    StatusText.Text        = "ERREUR";
                    StatusText.Foreground  = red;
                    StatusDetailText.Text  = "Echec de connexion";
                    ConnectButton.Content  = "REESSAYER";
                    ConnectButton.IsEnabled = true;
                    break;

                default: // DISCONNECTED
                    StatusText.Text        = "DECONNECTE";
                    StatusText.Foreground  = grey;
                    StatusDetailText.Text  = "Appuyez pour vous connecter";
                    ConnectButton.Content  = "CONNECT";
                    ConnectButton.IsEnabled = true;
                    break;
            }

            // Afficher le mode actif dans le label du bas
            if (ModeLabel != null)
                ModeLabel.Text = _vpnService.ActiveMode.Label();
        }
    }
}
