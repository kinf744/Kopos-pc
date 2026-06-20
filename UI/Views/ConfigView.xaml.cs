using KighmuVpnWindows.Models;
using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Vpn;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            RefreshProfilesList();
        }

        private void PopulateModeSelector()
        {
            ModeSelector.Items.Clear();
            foreach (TunnelMode mode in Enum.GetValues(typeof(TunnelMode)))
            {
                var item = new ComboBoxItem { Content = mode.Label(), Tag = mode };
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
                RefreshProfilesList();
            }
        }

        private void LoadFieldsForMode(TunnelMode mode)
        {
            DynamicFieldsPanel.Children.Clear();
            switch (mode)
            {
                case TunnelMode.SLOW_DNS:
                    AddField("Nom du profil",   "profileName", "Mon profil SlowDNS");
                    AddField("Serveur SSH",     "sshHost",     "ssh.example.com");
                    AddField("Port SSH",        "sshPort",     "22");
                    AddField("Utilisateur SSH", "sshUser",     "root");
                    AddField("Mot de passe",    "sshPass",     "", isPassword: true);
                    AddField("Serveur DNS",     "dnsServer",   "8.8.8.8");
                    AddField("Nameserver",      "nameserver",  "ns1.example.com");
                    AddField("Cle publique",    "publicKey",   "");
                    break;
                case TunnelMode.HTTP_PROXY:
                    AddField("Nom du profil",  "profileName",   "Mon profil HTTP");
                    AddField("Hote proxy",     "proxyHost",     "proxy.example.com");
                    AddField("Port proxy",     "proxyPort",     "8080");
                    AddField("Payload custom", "customPayload", "GET / HTTP/1.1[crlf]Host: [host][crlf][crlf]");
                    AddField("Serveur SSH",    "sshHost",       "ssh.example.com");
                    AddField("Port SSH",       "sshPort",       "22");
                    AddField("Utilisateur",    "sshUser",       "root");
                    AddField("Mot de passe",   "sshPass",       "", isPassword: true);
                    break;
                case TunnelMode.SSH_SSL_TLS:
                    AddField("Nom du profil", "profileName", "Mon profil SSL");
                    AddField("Serveur SSH",   "sshHost",     "ssh.example.com");
                    AddField("Port TLS",      "sshPort",     "443");
                    AddField("Utilisateur",   "sshUser",     "root");
                    AddField("Mot de passe",  "sshPass",     "", isPassword: true);
                    AddField("SNI",           "sni",         "example.com");
                    break;
                case TunnelMode.V2RAY_XRAY:
                    AddField("Nom du profil", "profileName",   "Mon profil Xray");
                    AddField("Serveur",       "serverAddress", "vpn.example.com");
                    AddField("Port",          "serverPort",    "443");
                    AddField("UUID",          "uuid",          "");
                    AddField("Protocol",      "protocol",      "vless");
                    AddField("Transport",     "transport",     "ws");
                    AddField("Path WS",       "wsPath",        "/");
                    AddField("SNI",           "sni",           "vpn.example.com");
                    AddJsonField("Ou coller config JSON Xray");
                    break;
                case TunnelMode.V2RAY_SLOWDNS:
                    AddField("Nom du profil",              "profileName", "Mon profil V2Ray+DNS");
                    AddLinkField("Lien V2Ray/Xray", "xrayLink", "vmess:// vless:// trojan:// ss://");
                    AddField("Serveur DNS",                "dnsServer",   "8.8.8.8");
                    AddField("Port DNS",                   "dnsPort",     "53");
                    AddField("Nameserver (dnstt target)",  "nameserver",  "ns1.example.com");
                    AddField("Cle publique",               "publicKey",   "");
                    AddSlider("Flux simultanes",            "tunnelCount", 1, 4, 1);
                    break;
                case TunnelMode.HYSTERIA_UDP:
                    AddField("Nom du profil",     "profileName",   "Mon profil Hysteria");
                    AddField("Serveur",           "serverAddress", "vpn.example.com");
                    AddField("Port",              "serverPort",    "36712");
                    AddField("Mot de passe",      "authPassword",  "", isPassword: true);
                    AddField("SNI",               "sni",           "vpn.example.com");
                    AddField("OBFS",              "obfs",          "");
                    AddField("Mot de passe OBFS", "obfsPassword",  "");
                    break;
            }
            var saveBtn = new Button
            {
                Content = "Enregistrer le profil",
                Margin  = new Thickness(0, 16, 0, 0),
                Style   = (Style)TryFindResource("KighmuButton")
            };
            saveBtn.Click += Save_Click;
            DynamicFieldsPanel.Children.Add(saveBtn);
        }

        private void AddField(string label, string tag, string placeholder, bool isPassword = false)
        {
            DynamicFieldsPanel.Children.Add(new TextBlock
            {
                Text       = label,
                Margin     = new Thickness(0, 8, 0, 2),
                Foreground = (System.Windows.Media.Brush)TryFindResource("TextSecondaryBrush")
                          ?? System.Windows.Media.Brushes.Gray
            });
            if (isPassword)
                DynamicFieldsPanel.Children.Add(new PasswordBox { Tag = tag });
            else
                DynamicFieldsPanel.Children.Add(new TextBox { Tag = tag, Text = placeholder });
        }

        private void AddLinkField(string label, string tag, string placeholder)
        {
            DynamicFieldsPanel.Children.Add(new TextBlock
            {
                Text       = label,
                Margin     = new Thickness(0, 8, 0, 2),
                Foreground = (System.Windows.Media.Brush)TryFindResource("TextSecondaryBrush")
                          ?? System.Windows.Media.Brushes.Gray
            });
            DynamicFieldsPanel.Children.Add(new TextBox
            {
                Tag                         = tag,
                Text                        = placeholder,
                AcceptsReturn               = false,
                Height                      = 60,
                TextWrapping                = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily                  = new System.Windows.Media.FontFamily("Consolas"),
                FontSize                    = 11
            });
        }

        private void AddSlider(string label, string tag, int min, int max, int defaultVal)
        {
            var lbl = new TextBlock
            {
                Text       = $"{label} : {defaultVal}",
                Margin     = new Thickness(0, 8, 0, 2),
                Foreground = (System.Windows.Media.Brush)TryFindResource("TextSecondaryBrush")
                          ?? System.Windows.Media.Brushes.Gray
            };
            DynamicFieldsPanel.Children.Add(lbl);
            var slider = new Slider
            {
                Tag      = tag,
                Minimum  = min,
                Maximum  = max,
                Value    = defaultVal,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin   = new Thickness(0, 0, 0, 4)
            };
            slider.ValueChanged += (s, e) => lbl.Text = $"{label} : {(int)slider.Value}";
            DynamicFieldsPanel.Children.Add(slider);
            DynamicFieldsPanel.Children.Add(new TextBlock
            {
                Text       = "1 flux = stable  |  2-3 flux = debit x N  |  4 flux = max",
                Foreground = (System.Windows.Media.Brush)TryFindResource("TextHintBrush")
                          ?? System.Windows.Media.Brushes.Gray,
                FontSize   = 11,
                Margin     = new Thickness(0, 0, 0, 8)
            });
        }

        private void AddJsonField(string label)
        {
            DynamicFieldsPanel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 2) });
            DynamicFieldsPanel.Children.Add(new TextBox
            {
                Tag                         = "xrayJson",
                AcceptsReturn               = true,
                Height                      = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily                  = new System.Windows.Media.FontFamily("Consolas"),
                FontSize                    = 11
            });
        }

        private int GetSliderValue(string tag, int defaultVal)
        {
            foreach (UIElement el in DynamicFieldsPanel.Children)
                if (el is Slider sl && sl.Tag?.ToString() == tag)
                    return (int)sl.Value;
            return defaultVal;
        }

        private string GetField(string tag)
        {
            foreach (UIElement el in DynamicFieldsPanel.Children)
            {
                if (el is TextBox tb && tb.Tag?.ToString() == tag) return tb.Text.Trim();
                if (el is PasswordBox pb && pb.Tag?.ToString() == tag) return pb.Password.Trim();
            }
            return "";
        }
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                switch (_selectedMode)
                {
                    case TunnelMode.SLOW_DNS:
                    {
                        var repo = new SlowDnsProfileRepository();
                        var p = new SlowDnsProfile
                        {
                            ProfileName = GetField("profileName"),
                            SshHost     = GetField("sshHost"),
                            SshPort     = int.TryParse(GetField("sshPort"), out var v1) ? v1 : 22,
                            SshUser     = GetField("sshUser"),
                            SshPass     = GetField("sshPass"),
                            DnsServer   = GetField("dnsServer"),
                            Nameserver  = GetField("nameserver"),
                            PublicKey   = GetField("publicKey")
                        };
                        if (string.IsNullOrWhiteSpace(p.ProfileName))
                            p.ProfileName = "SlowDNS " + DateTime.Now.ToString("HH:mm");
                        repo.Add(p);
                        break;
                    }
                    case TunnelMode.HTTP_PROXY:
                    {
                        var repo = new HttpProxyProfileRepository();
                        var p = new HttpProxyProfile
                        {
                            ProfileName   = GetField("profileName"),
                            ProxyHost     = GetField("proxyHost"),
                            ProxyPort     = int.TryParse(GetField("proxyPort"), out var v2) ? v2 : 8080,
                            CustomPayload = GetField("customPayload"),
                            SshHost       = GetField("sshHost"),
                            SshPort       = int.TryParse(GetField("sshPort"), out var v3) ? v3 : 22,
                            SshUser       = GetField("sshUser"),
                            SshPass       = GetField("sshPass")
                        };
                        if (string.IsNullOrWhiteSpace(p.ProfileName))
                            p.ProfileName = "HTTP " + DateTime.Now.ToString("HH:mm");
                        repo.Add(p);
                        break;
                    }
                    case TunnelMode.SSH_SSL_TLS:
                    {
                        var repo = new SshSslProfileRepository();
                        var p = new SshSslProfile
                        {
                            ProfileName = GetField("profileName"),
                            SshHost     = GetField("sshHost"),
                            SshPort     = int.TryParse(GetField("sshPort"), out var v4) ? v4 : 443,
                            SshUser     = GetField("sshUser"),
                            SshPass     = GetField("sshPass"),
                            Sni         = GetField("sni")
                        };
                        if (string.IsNullOrWhiteSpace(p.ProfileName))
                            p.ProfileName = "SSL " + DateTime.Now.ToString("HH:mm");
                        repo.Add(p);
                        break;
                    }
                    case TunnelMode.V2RAY_XRAY:
                    {
                        var repo = new XrayVpnProfileRepository();
                        var p = new XrayVpnProfile
                        {
                            ProfileName   = GetField("profileName"),
                            ServerAddress = GetField("serverAddress"),
                            ServerPort    = int.TryParse(GetField("serverPort"), out var v5) ? v5 : 443,
                            Uuid          = GetField("uuid"),
                            Protocol      = GetField("protocol"),
                            Transport     = GetField("transport"),
                            WsPath        = GetField("wsPath"),
                            Sni           = GetField("sni"),
                            XrayJson      = GetField("xrayJson")
                        };
                        if (string.IsNullOrWhiteSpace(p.ProfileName))
                            p.ProfileName = "Xray " + DateTime.Now.ToString("HH:mm");
                        repo.Add(p);
                        break;
                    }
                    case TunnelMode.V2RAY_SLOWDNS:
                    {
                        var repo = new XrayDnsProfileRepository();
                        var link = GetField("xrayLink").Trim();
                        var p = new XrayDnsProfile
                        {
                            ProfileName  = GetField("profileName"),
                            XrayLink     = link,
                            DnsServer    = GetField("dnsServer").Length > 0 ? GetField("dnsServer") : "8.8.8.8",
                            DnsPort      = int.TryParse(GetField("dnsPort"), out var vdp) ? vdp : 53,
                            Nameserver   = GetField("nameserver"),
                            PublicKey    = GetField("publicKey"),
                            TunnelCount  = GetSliderValue("tunnelCount", 1)
                        };
                        XrayDnsProfile.ParseLinkIntoProfile(link, p);
                        if (string.IsNullOrWhiteSpace(p.ProfileName))
                            p.ProfileName = "V2Ray+DNS " + DateTime.Now.ToString("HH:mm");
                        repo.Add(p);
                        break;
                    }
                    case TunnelMode.HYSTERIA_UDP:
                    {
                        var repo = new HysteriaProfileRepository();
                        var p = new HysteriaProfile
                        {
                            ProfileName   = GetField("profileName"),
                            ServerAddress = GetField("serverAddress"),
                            ServerPort    = int.TryParse(GetField("serverPort"), out var v7) ? v7 : 36712,
                            AuthPassword  = GetField("authPassword"),
                            Sni           = GetField("sni"),
                            Obfs          = GetField("obfs"),
                            ObfsPassword  = GetField("obfsPassword")
                        };
                        if (string.IsNullOrWhiteSpace(p.ProfileName))
                            p.ProfileName = "Hysteria " + DateTime.Now.ToString("HH:mm");
                        repo.Add(p);
                        break;
                    }
                }
                await _vpnService.SetMode(_selectedMode);
                RefreshProfilesList();
                MessageBox.Show("Profil enregistre avec succes.", "Succes",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshProfilesList()
        {
            ProfilesList.Items.Clear();
            foreach (var (id, name, selected) in GetProfileNamesForMode(_selectedMode))
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var chk = new CheckBox
                {
                    Content  = name,
                    IsChecked = selected,
                    Tag      = id,
                    Width    = 260,
                    Foreground = (System.Windows.Media.Brush)TryFindResource("TextPrimaryBrush")
                              ?? System.Windows.Media.Brushes.White,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                chk.Checked   += ProfileCheckbox_Changed;
                chk.Unchecked += ProfileCheckbox_Changed;
                var delBtn = new Button
                {
                    Content = "X",
                    Width   = 28, Height = 24,
                    Margin  = new Thickness(8, 0, 0, 0),
                    Tag     = id,
                    Style   = (Style)TryFindResource("MaterialDesignFlatButton")
                };
                delBtn.Click += DeleteProfile_Click;
                panel.Children.Add(chk);
                panel.Children.Add(delBtn);
                ProfilesList.Items.Add(new ListBoxItem { Content = panel });
            }
        }

        private List<(string id, string name, bool selected)> GetProfileNamesForMode(TunnelMode mode)
        {
            return mode switch
            {
                TunnelMode.SLOW_DNS      => new SlowDnsProfileRepository().GetAll()
                                            .Select(p => (p.Id, p.ProfileName, p.IsSelected)).ToList(),
                TunnelMode.HTTP_PROXY    => new HttpProxyProfileRepository().GetAll()
                                            .Select(p => (p.Id, p.ProfileName, p.IsSelected)).ToList(),
                TunnelMode.SSH_SSL_TLS   => new SshSslProfileRepository().GetAll()
                                            .Select(p => (p.Id, p.ProfileName, p.IsSelected)).ToList(),
                TunnelMode.V2RAY_XRAY    => new XrayVpnProfileRepository().GetAll()
                                            .Select(p => (p.Id, p.ProfileName, p.IsSelected)).ToList(),
                TunnelMode.V2RAY_SLOWDNS => new XrayDnsProfileRepository().GetAll()
                                            .Select(p => (p.Id, p.ProfileName, p.IsSelected)).ToList(),
                TunnelMode.HYSTERIA_UDP  => new HysteriaProfileRepository().GetAll()
                                            .Select(p => (p.Id, p.ProfileName, p.IsSelected)).ToList(),
                _ => new List<(string, string, bool)>()
            };
        }

        private void ProfileCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.Tag is string id)
                SetProfileSelected(id, chk.IsChecked == true);
        }

        private void SetProfileSelected(string id, bool selected)
        {
            switch (_selectedMode)
            {
                case TunnelMode.SLOW_DNS:      new SlowDnsProfileRepository().SetSelected(id, selected);   break;
                case TunnelMode.HTTP_PROXY:    new HttpProxyProfileRepository().SetSelected(id, selected); break;
                case TunnelMode.SSH_SSL_TLS:   new SshSslProfileRepository().SetSelected(id);              break;
                case TunnelMode.V2RAY_XRAY:    new XrayVpnProfileRepository().SetSelected(id, selected);   break;
                case TunnelMode.V2RAY_SLOWDNS: new XrayDnsProfileRepository().SetSelected(id, selected);   break;
                case TunnelMode.HYSTERIA_UDP:  new HysteriaProfileRepository().SetSelected(id, selected);  break;
            }
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                if (MessageBox.Show("Supprimer ce profil ?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                switch (_selectedMode)
                {
                    case TunnelMode.SLOW_DNS:      new SlowDnsProfileRepository().Delete(id);    break;
                    case TunnelMode.HTTP_PROXY:    new HttpProxyProfileRepository().Delete(id);  break;
                    case TunnelMode.SSH_SSL_TLS:   new SshSslProfileRepository().Delete(id);     break;
                    case TunnelMode.V2RAY_XRAY:    new XrayVpnProfileRepository().Delete(id);    break;
                    case TunnelMode.V2RAY_SLOWDNS: new XrayDnsProfileRepository().Delete(id);    break;
                    case TunnelMode.HYSTERIA_UDP:  new HysteriaProfileRepository().Delete(id);   break;
                }
                RefreshProfilesList();
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Profil KIGHMU (*.kighmu)|*.kighmu|JSON (*.json)|*.json|Tous|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                switch (_selectedMode)
                {
                    case TunnelMode.SLOW_DNS:      new SlowDnsProfileRepository().Add(SlowDnsProfile.FromJson(json));     break;
                    case TunnelMode.HTTP_PROXY:    new HttpProxyProfileRepository().Add(HttpProxyProfile.FromJson(json)); break;
                    case TunnelMode.SSH_SSL_TLS:   new SshSslProfileRepository().Add(SshSslProfile.FromJson(json));       break;
                    case TunnelMode.V2RAY_XRAY:    new XrayVpnProfileRepository().Add(XrayVpnProfile.FromJson(json));     break;
                    case TunnelMode.V2RAY_SLOWDNS: new XrayDnsProfileRepository().Add(XrayDnsProfile.FromJson(json));     break;
                    case TunnelMode.HYSTERIA_UDP:  new HysteriaProfileRepository().Add(HysteriaProfile.FromJson(json));   break;
                }
                RefreshProfilesList();
                MessageBox.Show("Profil importe avec succes.", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur import: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter   = "Profil KIGHMU (*.kighmu)|*.kighmu",
                FileName = $"kighmu_{_selectedMode}_{DateTime.Now:yyyyMMdd_HHmm}"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string? json = _selectedMode switch
                {
                    TunnelMode.SLOW_DNS      => new SlowDnsProfileRepository().GetSelected().FirstOrDefault()?.ToJson(),
                    TunnelMode.HTTP_PROXY    => new HttpProxyProfileRepository().GetSelected().FirstOrDefault()?.ToJson(),
                    TunnelMode.SSH_SSL_TLS   => new SshSslProfileRepository().GetActive()?.ToJson(),
                    TunnelMode.V2RAY_XRAY    => new XrayVpnProfileRepository().GetSelected().FirstOrDefault()?.ToJson(),
                    TunnelMode.V2RAY_SLOWDNS => new XrayDnsProfileRepository().GetSelected().FirstOrDefault()?.ToJson(),
                    TunnelMode.HYSTERIA_UDP  => new HysteriaProfileRepository().GetSelected().FirstOrDefault()?.ToJson(),
                    _ => null
                };
                if (json == null) { MessageBox.Show("Aucun profil selectionne.", "Export"); return; }
                File.WriteAllText(dlg.FileName, json);
                MessageBox.Show("Profil exporte avec succes.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur export: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
