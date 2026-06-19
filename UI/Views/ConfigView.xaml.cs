using KighmuVpnWindows.Models;
using KighmuVpnWindows.Vpn;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class ConfigView : UserControl
    {
        private readonly KighmuVpnService _vpnService = KighmuVpnService.Instance;
        private TunnelMode _selectedMode;

        public ConfigView()
        {
            InitializeComponent();
            _selectedMode = _vpnService.ActiveMode;
            PopulateModeSelector();
            LoadFieldsForMode(_selectedMode);
        }

        // ── Selecteur de mode ─────────────────────────────────────────────────

        private void PopulateModeSelector()
        {
            ModeSelector.Items.Clear();
            foreach (TunnelMode mode in Enum.GetValues(typeof(TunnelMode)))
            {
                var item = new ComboBoxItem
                {
                    Content = mode.Label(),
                    Tag     = mode
                };
                ModeSelector.Items.Add(item);
                if (mode == _selectedMode)
                    ModeSelector.SelectedItem = item;
            }
        }

        private void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeSelector.SelectedItem is ComboBoxItem item && item.Tag is TunnelMode mode)
            {
                _selectedMode = mode;
                LoadFieldsForMode(mode);
            }
        }

        // ── Champs dynamiques par mode ────────────────────────────────────────

        private void LoadFieldsForMode(TunnelMode mode)
        {
            DynamicFieldsPanel.Children.Clear();

            switch (mode)
            {
                case TunnelMode.SLOW_DNS:
                    AddField("Serveur SSH",     "sshHost",   "ex: ssh.example.com");
                    AddField("Port SSH",        "sshPort",   "22");
                    AddField("Utilisateur SSH", "sshUser",   "root");
                    AddField("Mot de passe",    "sshPass",   "", isPassword: true);
                    AddField("Serveur DNS",     "dnsServer", "8.8.8.8");
                    AddField("Nameserver",      "nameserver","ns1.example.com");
                    AddField("Cle publique",    "publicKey", "");
                    break;

                case TunnelMode.HTTP_PROXY:
                    AddField("Hote proxy",      "proxyHost",     "proxy.example.com");
                    AddField("Port proxy",      "proxyPort",     "8080");
                    AddField("Payload custom",  "customPayload", "GET / HTTP/1.1[crlf]Host: [host][crlf][crlf]");
                    break;

                case TunnelMode.SSH_SSL_TLS:
                    AddField("Serveur SSH",     "sshHost",   "ssh.example.com");
                    AddField("Port TLS",        "sshPort",   "443");
                    AddField("Utilisateur",     "sshUser",   "root");
                    AddField("Mot de passe",    "sshPass",   "", isPassword: true);
                    AddField("SNI",             "sni",       "example.com");
                    break;

                case TunnelMode.V2RAY_XRAY:
                    AddField("Serveur",         "serverAddress", "vpn.example.com");
                    AddField("Port",            "serverPort",    "443");
                    AddField("UUID",            "uuid",          "");
                    AddField("Protocol",        "protocol",      "vless");
                    AddField("Transport",       "transport",     "ws");
                    AddField("Path WS",         "wsPath",        "/");
                    AddField("SNI",             "sni",           "vpn.example.com");
                    AddJsonField("Ou coller config JSON Xray");
                    break;

                case TunnelMode.V2RAY_SLOWDNS:
                    AddField("Serveur DNS dnstt","dnsServer",  "8.8.8.8");
                    AddField("Nameserver",       "nameserver", "ns1.example.com");
                    AddField("Cle publique",     "publicKey",  "");
                    AddField("Serveur Xray",     "serverAddress","vpn.example.com");
                    AddField("Port Xray",        "serverPort", "443");
                    AddField("UUID",             "uuid",       "");
                    break;

                case TunnelMode.HYSTERIA_UDP:
                    AddField("Serveur",         "serverAddress","vpn.example.com");
                    AddField("Port",            "serverPort",   "36712");
                    AddField("Mot de passe",    "authPassword", "", isPassword: true);
                    AddField("SNI",             "sni",          "vpn.example.com");
                    AddField("OBFS",            "obfs",         "");
                    break;
            }

            // Bouton Sauvegarder en bas
            var saveBtn = new Button
            {
                Content = "Sauvegarder le profil",
                Margin  = new Thickness(0, 16, 0, 0),
                Style   = (Style)TryFindResource("PrimaryButtonStyle")
            };
            saveBtn.Click += Save_Click;
            DynamicFieldsPanel.Children.Add(saveBtn);
        }

        // ── Helpers UI ────────────────────────────────────────────────────────

        private void AddField(string label, string tag, string placeholder, bool isPassword = false)
        {
            var lbl = new TextBlock
            {
                Text       = label,
                Margin     = new Thickness(0, 8, 0, 2),
                Foreground = (System.Windows.Media.Brush)TryFindResource("TextSecondaryBrush")
                          ?? System.Windows.Media.Brushes.Gray
            };
            DynamicFieldsPanel.Children.Add(lbl);

            if (isPassword)
            {
                var pb = new PasswordBox { Tag = tag };
                DynamicFieldsPanel.Children.Add(pb);
            }
            else
            {
                var tb = new TextBox { Tag = tag, Text = placeholder };
                DynamicFieldsPanel.Children.Add(tb);
            }
        }

        private void AddJsonField(string label)
        {
            var lbl = new TextBlock
            {
                Text   = label,
                Margin = new Thickness(0, 12, 0, 2)
            };
            DynamicFieldsPanel.Children.Add(lbl);

            var tb = new TextBox
            {
                Tag             = "xrayJson",
                AcceptsReturn   = true,
                Height          = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily      = new System.Windows.Media.FontFamily("Consolas"),
                FontSize        = 11
            };
            DynamicFieldsPanel.Children.Add(tb);
        }

        // ── Actions ───────────────────────────────────────────────────────────

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vpnService.SetMode(_selectedMode);
                MessageBox.Show(
                    $"Mode '{_selectedMode.Label()}' sauvegarde.",
                    "Succes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Profil KIGHMU (*.kighmu)|*.kighmu|JSON (*.json)|*.json|Tous|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                // TODO: parser et importer via ProfileRepository
                MessageBox.Show($"Import: {dlg.FileName}", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "Profil KIGHMU (*.kighmu)|*.kighmu",
                FileName = $"kighmu_profil_{_selectedMode}"
            };
            if (dlg.ShowDialog() == true)
            {
                // TODO: exporter via ProfileRepository
                MessageBox.Show($"Export: {dlg.FileName}", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
