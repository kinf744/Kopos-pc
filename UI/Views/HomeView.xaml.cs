using System.Windows;
using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class HomeView : UserControl
    {
        private bool _isConnected = false;

        public HomeView()
        {
            InitializeComponent();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: brancher sur Vpn/KighmuVpnService.cs (StartTunnel / StopTunnel)
            _isConnected = !_isConnected;

            if (_isConnected)
            {
                StatusText.Text = "CONNECTÉ";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentGreenBrush");
                StatusDetailText.Text = "Tunnel actif";
                ConnectButton.Content = "DISCONNECT";
            }
            else
            {
                StatusText.Text = "DÉCONNECTÉ";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                StatusDetailText.Text = "Appuyez pour vous connecter";
                ConnectButton.Content = "CONNECT";
            }
        }
    }
}
