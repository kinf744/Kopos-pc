using KighmuVpnWindows.Models;
using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.UI.Dialogs;
using KighmuVpnWindows.Vpn;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows.UI.Views
{
    public partial class ConfigView : UserControl
    {
        private readonly KighmuVpnService _vpnService = KighmuVpnService.Instance;
        private TunnelMode _selectedMode;
        private readonly SlowDnsProfileRepository    _slowDnsRepo    = new SlowDnsProfileRepository();
        private readonly HysteriaProfileRepository   _hysteriaRepo   = new HysteriaProfileRepository();
        private readonly XrayDnsProfileRepository      _xrayDnsRepo    = new XrayDnsProfileRepository();
        private readonly HttpProxyProfileRepository    _httpProxyRepo  = new HttpProxyProfileRepository();
        private readonly XrayVpnProfileRepository      _xrayVpnRepo    = new XrayVpnProfileRepository();

        // Correspondance index d'onglet -> TunnelMode (sans ZIVPN, retire pour Windows)
        private readonly TunnelMode[] _tabModes = new[]
        {
            TunnelMode.SLOW_DNS,
            TunnelMode.HTTP_PROXY,
            TunnelMode.SSH_SSL_TLS,
            TunnelMode.V2RAY_XRAY,
            TunnelMode.V2RAY_SLOWDNS,
            TunnelMode.HYSTERIA_UDP
        };

        private Button[] _tabButtons = Array.Empty<Button>();
        private StackPanel[] _panels = Array.Empty<StackPanel>();

        public ConfigView()
        {
            InitializeComponent();

            _tabButtons = new[] { TabSlowDns, TabHttp, TabSsl, TabXray, TabV2Dns, TabHysteria };
            _panels     = new[] { PanelSlowDns, PanelHttp, PanelSsl, PanelXray, PanelV2Dns, PanelHysteria };

            _selectedMode = _vpnService.ActiveMode;
            int startIndex = Array.IndexOf(_tabModes, _selectedMode);
            if (startIndex < 0) startIndex = 0;

            SelectTab(startIndex);
        }

        // ── Onglets (equivalent de selectTab() dans ConfigFragment.kt) ─────────
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
                SelectTab(index);
        }

        private void SelectTab(int index)
        {
            if (index < 0 || index >= _tabModes.Length) return;

            _selectedMode = _tabModes[index];

            var active   = (Brush)TryFindResource("AccentBlueBrush")      ?? Brushes.DodgerBlue;
            var inactive = (Brush)TryFindResource("SurfaceCardBrush")     ?? Brushes.DimGray;
            var textCol  = (Brush)TryFindResource("TextPrimaryBrush")     ?? Brushes.White;

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i].Background = i == index ? active : inactive;
                _tabButtons[i].Foreground = textCol;
            }

            for (int i = 0; i < _panels.Length; i++)
            {
                _panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_selectedMode == TunnelMode.SLOW_DNS)
                RefreshSlowDnsList();
            else if (_selectedMode == TunnelMode.HYSTERIA_UDP)
                RefreshHysteriaList();
            else if (_selectedMode == TunnelMode.V2RAY_SLOWDNS)
                RefreshV2DnsList();
            else if (_selectedMode == TunnelMode.HTTP_PROXY)
                RefreshHttpProxyList();
            else if (_selectedMode == TunnelMode.V2RAY_XRAY)
                RefreshXrayList();

            // TODO (prochaines etapes) : charger les champs / la liste des autres modes ici
        }

        // ── SlowDNS : liste de profils (equivalent SlowDnsProfileAdapter.kt) ───
        // NB: ces actions ne touchent jamais _vpnService.SetMode() volontairement :
        // SetMode() coupe le tunnel s'il est connecte, et Android ne deconnecte
        // jamais le tunnel lors d'une simple selection/edition/suppression de profil.
        private void RefreshSlowDnsList()
        {
            SlowDnsProfilesList.Children.Clear();
            var profiles = _slowDnsRepo.GetAll();
            for (int i = 0; i < profiles.Count; i++)
            {
                SlowDnsProfilesList.Children.Add(BuildSlowDnsRow(profiles[i], i));
            }
        }

        private UIElement BuildSlowDnsRow(SlowDnsProfile p, int index)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var checkBox = new CheckBox
            {
                IsChecked = p.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            checkBox.Checked   += (s, e) => _slowDnsRepo.UpdateSelection(p.Id, true);
            checkBox.Unchecked += (s, e) => _slowDnsRepo.UpdateSelection(p.Id, false);
            Grid.SetColumn(checkBox, 0);

            var textPanel = new StackPanel();
            var name = string.IsNullOrWhiteSpace(p.ProfileName) ? $"Profil {index + 1}" : p.ProfileName;
            textPanel.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = (Brush)TryFindResource("TextPrimaryBrush") ?? Brushes.White,
                FontSize = 14
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = $"{p.SshHost}  DNS: {p.DnsServer}",
                Foreground = (Brush)TryFindResource("TextHintBrush") ?? Brushes.Gray,
                FontSize = 11
            });
            Grid.SetColumn(textPanel, 1);

            // Clic droit = equivalent du long-press Android (Modifier / Cloner / Supprimer)
            var menu = new ContextMenu();
            var editItem = new MenuItem { Header = "Modifier" };
            editItem.Click += (s, e) => EditSlowDnsProfile(p);
            var cloneItem = new MenuItem { Header = "Cloner" };
            cloneItem.Click += (s, e) => { _slowDnsRepo.Clone(p.Id); RefreshSlowDnsList(); };
            var deleteItem = new MenuItem { Header = "Supprimer" };
            deleteItem.Click += (s, e) =>
            {
                if (MessageBox.Show($"Supprimer '{name}' ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _slowDnsRepo.Delete(p.Id);
                RefreshSlowDnsList();
            };
            menu.Items.Add(editItem);
            menu.Items.Add(cloneItem);
            menu.Items.Add(deleteItem);
            row.ContextMenu = menu;

            row.Children.Add(checkBox);
            row.Children.Add(textPanel);
            return row;
        }

        private void EditSlowDnsProfile(SlowDnsProfile p)
        {
            SlowDnsProfileEditDialog.Show(Window.GetWindow(this), p, updated =>
            {
                _slowDnsRepo.Update(updated);
                RefreshSlowDnsList();
            });
        }

        private void BtnAddSlowDns_Click(object sender, RoutedEventArgs e)
        {
            SlowDnsProfileEditDialog.Show(Window.GetWindow(this), null, newProfile =>
            {
                _slowDnsRepo.Add(newProfile);
                RefreshSlowDnsList();
            });
        }

        // ── Hysteria : liste de profils ─────────────────────────────────────────
        private void RefreshHysteriaList()
        {
            HysteriaProfilesList.Children.Clear();
            var profiles = _hysteriaRepo.GetAll();
            for (int i = 0; i < profiles.Count; i++)
                HysteriaProfilesList.Children.Add(BuildHysteriaRow(profiles[i], i));
        }

        private UIElement BuildHysteriaRow(HysteriaProfile p, int index)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var checkBox = new CheckBox
            {
                IsChecked         = p.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            };
            checkBox.Checked   += (s, e) => _hysteriaRepo.UpdateSelection(p.Id, true);
            checkBox.Unchecked += (s, e) => _hysteriaRepo.UpdateSelection(p.Id, false);
            Grid.SetColumn(checkBox, 0);

            var textPanel = new StackPanel();
            var name = string.IsNullOrWhiteSpace(p.ProfileName) ? $"Profil {index + 1}" : p.ProfileName;
            textPanel.Children.Add(new TextBlock
            {
                Text       = name,
                Foreground = (Brush)TryFindResource("TextPrimaryBrush") ?? Brushes.White,
                FontSize   = 14
            });
            textPanel.Children.Add(new TextBlock
            {
                Text       = $"{p.ServerAddress}:{p.ServerPort}  Up:{p.UploadMbps}M  Down:{p.DownloadMbps}M",
                Foreground = (Brush)TryFindResource("TextHintBrush") ?? Brushes.Gray,
                FontSize   = 11
            });
            Grid.SetColumn(textPanel, 1);

            var menu       = new ContextMenu();
            var editItem   = new MenuItem { Header = "Modifier" };
            editItem.Click += (s, e) => EditHysteriaProfile(p);
            var cloneItem   = new MenuItem { Header = "Cloner" };
            cloneItem.Click += (s, e) => { _hysteriaRepo.Clone(p.Id); RefreshHysteriaList(); };
            var deleteItem   = new MenuItem { Header = "Supprimer" };
            deleteItem.Click += (s, e) =>
            {
                if (MessageBox.Show($"Supprimer '{name}' ?", "Confirmation",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _hysteriaRepo.Delete(p.Id);
                RefreshHysteriaList();
            };
            menu.Items.Add(editItem);
            menu.Items.Add(cloneItem);
            menu.Items.Add(deleteItem);
            row.ContextMenu = menu;

            row.Children.Add(checkBox);
            row.Children.Add(textPanel);
            return row;
        }

        private void EditHysteriaProfile(HysteriaProfile p)
        {
            HysteriaProfileEditDialog.Show(Window.GetWindow(this), p, updated =>
            {
                _hysteriaRepo.Update(updated);
                RefreshHysteriaList();
            });
        }

        private void BtnAddHysteria_Click(object sender, RoutedEventArgs e)
        {
            HysteriaProfileEditDialog.Show(Window.GetWindow(this), null, newProfile =>
            {
                _hysteriaRepo.Add(newProfile);
                RefreshHysteriaList();
            });
        }

        // ── V2Ray+DNS : liste de profils ────────────────────────────────────────
        private void RefreshV2DnsList()
        {
            V2DnsProfilesList.Children.Clear();
            var profiles = _xrayDnsRepo.GetAll();
            for (int i = 0; i < profiles.Count; i++)
                V2DnsProfilesList.Children.Add(BuildV2DnsRow(profiles[i], i));
        }

        private UIElement BuildV2DnsRow(XrayDnsProfile p, int index)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var checkBox = new CheckBox
            {
                IsChecked         = p.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            };
            checkBox.Checked   += (s, e) => _xrayDnsRepo.UpdateSelection(p.Id, true);
            checkBox.Unchecked += (s, e) => _xrayDnsRepo.UpdateSelection(p.Id, false);
            Grid.SetColumn(checkBox, 0);

            var textPanel = new StackPanel();
            var name = string.IsNullOrWhiteSpace(p.ProfileName) ? $"Profil {index + 1}" : p.ProfileName;
            textPanel.Children.Add(new TextBlock
            {
                Text       = name,
                Foreground = (Brush)TryFindResource("TextPrimaryBrush") ?? Brushes.White,
                FontSize   = 14
            });
            textPanel.Children.Add(new TextBlock
            {
                Text       = $"{p.Protocol}  {p.ServerAddress}:{p.ServerPort}  DNS:{p.DnsServer}",
                Foreground = (Brush)TryFindResource("TextHintBrush") ?? Brushes.Gray,
                FontSize   = 11
            });
            Grid.SetColumn(textPanel, 1);

            var menu        = new ContextMenu();
            var editItem    = new MenuItem { Header = "Modifier" };
            editItem.Click += (s, e) => EditV2DnsProfile(p);
            var cloneItem    = new MenuItem { Header = "Cloner" };
            cloneItem.Click += (s, e) => { _xrayDnsRepo.Clone(p.Id); RefreshV2DnsList(); };
            var deleteItem    = new MenuItem { Header = "Supprimer" };
            deleteItem.Click += (s, e) =>
            {
                if (MessageBox.Show($"Supprimer '{name}' ?", "Confirmation",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _xrayDnsRepo.Delete(p.Id);
                RefreshV2DnsList();
            };
            menu.Items.Add(editItem);
            menu.Items.Add(cloneItem);
            menu.Items.Add(deleteItem);
            row.ContextMenu = menu;

            row.Children.Add(checkBox);
            row.Children.Add(textPanel);
            return row;
        }

        private void EditV2DnsProfile(XrayDnsProfile p)
        {
            XrayDnsProfileEditDialog.Show(Window.GetWindow(this), p, updated =>
            {
                _xrayDnsRepo.Update(updated);
                RefreshV2DnsList();
            });
        }

        private void BtnAddV2Dns_Click(object sender, RoutedEventArgs e)
        {
            XrayDnsProfileEditDialog.Show(Window.GetWindow(this), null, newProfile =>
            {
                _xrayDnsRepo.Add(newProfile);
                RefreshV2DnsList();
            });
        }

        // ── HTTP Proxy : liste de profils ───────────────────────────────────────
        private void RefreshHttpProxyList()
        {
            HttpProfilesList.Children.Clear();
            var profiles = _httpProxyRepo.GetAll();
            for (int i = 0; i < profiles.Count; i++)
                HttpProfilesList.Children.Add(BuildHttpProxyRow(profiles[i], i));
        }

        private UIElement BuildHttpProxyRow(HttpProxyProfile p, int index)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var checkBox = new CheckBox
            {
                IsChecked         = p.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            };
            checkBox.Checked   += (s, e) => _httpProxyRepo.UpdateSelection(p.Id, true);
            checkBox.Unchecked += (s, e) => _httpProxyRepo.UpdateSelection(p.Id, false);
            Grid.SetColumn(checkBox, 0);

            var textPanel = new StackPanel();
            var name = string.IsNullOrWhiteSpace(p.ProfileName) ? $"Profil {index + 1}" : p.ProfileName;
            textPanel.Children.Add(new TextBlock
            {
                Text       = name,
                Foreground = (Brush)TryFindResource("TextPrimaryBrush") ?? Brushes.White,
                FontSize   = 14
            });
            textPanel.Children.Add(new TextBlock
            {
                Text       = $"{p.SshHost}:{p.SshPort}  Proxy: {p.ProxyHost}:{p.ProxyPort}",
                Foreground = (Brush)TryFindResource("TextHintBrush") ?? Brushes.Gray,
                FontSize   = 11
            });
            Grid.SetColumn(textPanel, 1);

            var menu        = new ContextMenu();
            var editItem    = new MenuItem { Header = "Modifier" };
            editItem.Click += (s, e) => EditHttpProxyProfile(p);
            var cloneItem    = new MenuItem { Header = "Cloner" };
            cloneItem.Click += (s, e) => { _httpProxyRepo.Clone(p.Id); RefreshHttpProxyList(); };
            var deleteItem    = new MenuItem { Header = "Supprimer" };
            deleteItem.Click += (s, e) =>
            {
                if (MessageBox.Show($"Supprimer '{name}' ?", "Confirmation",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _httpProxyRepo.Delete(p.Id);
                RefreshHttpProxyList();
            };
            menu.Items.Add(editItem);
            menu.Items.Add(cloneItem);
            menu.Items.Add(deleteItem);
            row.ContextMenu = menu;

            row.Children.Add(checkBox);
            row.Children.Add(textPanel);
            return row;
        }

        private void EditHttpProxyProfile(HttpProxyProfile p)
        {
            HttpProxyProfileEditDialog.Show(Window.GetWindow(this), p, updated =>
            {
                _httpProxyRepo.Update(updated);
                RefreshHttpProxyList();
            });
        }

        private void BtnAddHttp_Click(object sender, RoutedEventArgs e)
        {
            HttpProxyProfileEditDialog.Show(Window.GetWindow(this), null, newProfile =>
            {
                _httpProxyRepo.Add(newProfile);
                RefreshHttpProxyList();
            });
        }

        // ── V2Ray/Xray : liste de profils ───────────────────────────────────────
        private void RefreshXrayList()
        {
            XrayProfilesList.Children.Clear();
            var profiles = _xrayVpnRepo.GetAll();
            for (int i = 0; i < profiles.Count; i++)
                XrayProfilesList.Children.Add(BuildXrayRow(profiles[i], i));
        }

        private UIElement BuildXrayRow(XrayVpnProfile p, int index)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var checkBox = new CheckBox
            {
                IsChecked         = p.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            };
            checkBox.Checked   += (s, e) => _xrayVpnRepo.UpdateSelection(p.Id, true);
            checkBox.Unchecked += (s, e) => _xrayVpnRepo.UpdateSelection(p.Id, false);
            Grid.SetColumn(checkBox, 0);

            var textPanel = new StackPanel();
            var name = string.IsNullOrWhiteSpace(p.ProfileName) ? $"Profil {index + 1}" : p.ProfileName;
            textPanel.Children.Add(new TextBlock
            {
                Text       = name,
                Foreground = (Brush)TryFindResource("TextPrimaryBrush") ?? Brushes.White,
                FontSize   = 14
            });
            textPanel.Children.Add(new TextBlock
            {
                Text       = $"{p.Protocol}  {p.ServerAddress}:{p.ServerPort}  {p.Transport}",
                Foreground = (Brush)TryFindResource("TextHintBrush") ?? Brushes.Gray,
                FontSize   = 11
            });
            Grid.SetColumn(textPanel, 1);

            var menu        = new ContextMenu();
            var editItem    = new MenuItem { Header = "Modifier" };
            editItem.Click += (s, e) => EditXrayProfile(p);
            var cloneItem    = new MenuItem { Header = "Cloner" };
            cloneItem.Click += (s, e) => { _xrayVpnRepo.Clone(p.Id); RefreshXrayList(); };
            var deleteItem    = new MenuItem { Header = "Supprimer" };
            deleteItem.Click += (s, e) =>
            {
                if (MessageBox.Show($"Supprimer '{name}' ?", "Confirmation",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _xrayVpnRepo.Delete(p.Id);
                RefreshXrayList();
            };
            menu.Items.Add(editItem);
            menu.Items.Add(cloneItem);
            menu.Items.Add(deleteItem);
            row.ContextMenu = menu;

            row.Children.Add(checkBox);
            row.Children.Add(textPanel);
            return row;
        }

        private void EditXrayProfile(XrayVpnProfile p)
        {
            XrayVpnProfileEditDialog.Show(Window.GetWindow(this), p, updated =>
            {
                _xrayVpnRepo.Update(updated);
                RefreshXrayList();
            });
        }

        private void BtnAddXray_Click(object sender, RoutedEventArgs e)
        {
            XrayVpnProfileEditDialog.Show(Window.GetWindow(this), null, newProfile =>
            {
                _xrayVpnRepo.Add(newProfile);
                RefreshXrayList();
            });
        }

        // ── Import / Export / Save : seront reconnectes panel par panel dans les etapes suivantes ──
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Import : sera reconnecte lors de la construction de ce panel.", "A venir");
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Export : sera reconnecte lors de la construction de ce panel.", "A venir");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Enregistrer : sera reconnecte lors de la construction de ce panel.", "A venir");
        }
    }
}
