using KighmuVpnWindows.Profiles;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows.UI.Dialogs
{
    /// <summary>Dialogue edition profil V2Ray+SlowDNS (XrayDnsProfile)</summary>
    public static class XrayDnsProfileEditDialog
    {
        public static void Show(Window? owner, XrayDnsProfile? profile, Action<XrayDnsProfile> onSave)
        {
            bool isEdit = profile != null;
            var p = profile ?? new XrayDnsProfile();

            var textBrush   = (Brush)Application.Current.TryFindResource("TextPrimaryBrush")   ?? Brushes.White;
            var hintBrush   = (Brush)Application.Current.TryFindResource("TextHintBrush")       ?? Brushes.Gray;
            var accentBrush = (Brush)Application.Current.TryFindResource("AccentBlueBrush")     ?? Brushes.DodgerBlue;
            var fieldBg     = (Brush)Application.Current.TryFindResource("SurfaceCardBrush")    ?? Brushes.DarkSlateGray;
            var bgBrush     = (Brush)Application.Current.TryFindResource("BackgroundDarkBrush") ?? Brushes.Black;

            var window = new Window
            {
                Title  = isEdit ? "Modifier profil V2Ray+DNS" : "Nouveau profil V2Ray+DNS",
                Width  = 460,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner      = owner,
                Background = bgBrush,
                ResizeMode = ResizeMode.NoResize
            };

            var outer  = new DockPanel();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var layout = new StackPanel { Margin = new Thickness(20) };
            scroll.Content = layout;

            void Label(string text)
            {
                layout.Children.Add(new TextBlock
                {
                    Text       = text,
                    Foreground = accentBrush,
                    FontSize   = 12,
                    FontWeight = FontWeights.Bold,
                    Margin     = new Thickness(0, 16, 0, 4)
                });
            }

            TextBox Field(string label, string value)
            {
                layout.Children.Add(new TextBlock
                {
                    Text       = label,
                    Foreground = hintBrush,
                    FontSize   = 10,
                    Margin     = new Thickness(0, 4, 0, 2)
                });
                var tb = new TextBox
                {
                    Text            = value,
                    Foreground      = textBrush,
                    Background      = fieldBg,
                    Padding         = new Thickness(8),
                    BorderThickness = new Thickness(0)
                };
                layout.Children.Add(tb);
                return tb;
            }

            CheckBox CheckField(string label, bool value)
            {
                var cb = new CheckBox
                {
                    Content    = label,
                    IsChecked  = value,
                    Foreground = textBrush,
                    Margin     = new Thickness(0, 6, 0, 2)
                };
                layout.Children.Add(cb);
                return cb;
            }

            // ── PROFILE ──────────────────────────────────────────────────────
            Label("PROFILE");
            var etName = Field("Profile Name", p.ProfileName);

            // ── LIEN XRAY ────────────────────────────────────────────────────
            Label("LIEN XRAY (vmess:// / vless:// / trojan://)");
            layout.Children.Add(new TextBlock
            {
                Text         = "Coller le lien puis cliquer Parser pour remplir automatiquement",
                Foreground   = hintBrush,
                FontSize     = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 4)
            });

            // Champ lien + bouton Parser sur la meme ligne
            var linkRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var etLink = new TextBox
            {
                Text            = p.XrayLink,
                Foreground      = textBrush,
                Background      = fieldBg,
                Padding         = new Thickness(8),
                BorderThickness = new Thickness(0)
            };
            var btnParse = new Button
            {
                Content    = "Parser",
                Padding    = new Thickness(10, 8, 10, 8),
                Margin     = new Thickness(6, 0, 0, 0),
                Background = accentBrush,
                Foreground = Brushes.White
            };
            Grid.SetColumn(etLink,   0);
            Grid.SetColumn(btnParse, 1);
            linkRow.Children.Add(etLink);
            linkRow.Children.Add(btnParse);
            layout.Children.Add(linkRow);

            // ── CONFIG XRAY (remplie apres parsing) ──────────────────────────
            Label("CONFIG XRAY");
            var etProto  = Field("Protocol (vmess / vless / trojan)", p.Protocol);
            var etHost   = Field("Server Address", p.ServerAddress);
            var etPort   = Field("Server Port", p.ServerPort.ToString());
            var etUuid   = Field("UUID / Password", p.Uuid);
            var etTrans  = Field("Transport (ws / grpc / tcp / xhttp)", p.Transport);
            var etPath   = Field("WS Path / gRPC Service", p.WsPath);
            var etWsHost = Field("WS Host (Host header)", p.WsHost);
            var cbTls    = CheckField("TLS active", p.Tls);
            var etSni    = Field("SNI", p.Sni);
            var cbInsec  = CheckField("Allow Insecure", p.AllowInsecure);

            // Action Parser : remplit les champs automatiquement
            btnParse.Click += (s, e) =>
            {
                var link = etLink.Text.Trim();
                if (string.IsNullOrWhiteSpace(link)) return;
                var tmp = new XrayDnsProfile();
                XrayDnsProfile.ParseLinkIntoProfile(link, tmp);
                etProto.Text  = tmp.Protocol;
                etHost.Text   = tmp.ServerAddress;
                etPort.Text   = tmp.ServerPort.ToString();
                etUuid.Text   = tmp.Uuid;
                etTrans.Text  = tmp.Transport;
                etPath.Text   = tmp.WsPath;
                etWsHost.Text = tmp.WsHost;
                cbTls.IsChecked  = tmp.Tls;
                etSni.Text    = tmp.Sni;
                cbInsec.IsChecked = tmp.AllowInsecure;
            };

            // ── SLOWDNS TRANSPORT ─────────────────────────────────────────────
            Label("SLOWDNS TRANSPORT");
            var etDns    = Field("DNS Server", p.DnsServer);
            var etNs     = Field("Nameserver (domaine dnstt)", p.Nameserver);
            var etPubKey = Field("Public Key", p.PublicKey);

            // ── TUNNELS PARALLELES ────────────────────────────────────────────
            Label("TUNNELS PARALLELES");
            var tvCount = new TextBlock
            {
                Text       = $"Flux simultanes : {p.TunnelCount}",
                Foreground = textBrush,
                FontSize   = 13,
                Margin     = new Thickness(0, 4, 0, 4)
            };
            layout.Children.Add(tvCount);
            var slider = new Slider
            {
                Minimum             = 1,
                Maximum             = 4,
                Value               = Math.Min(Math.Max(p.TunnelCount, 1), 4),
                IsSnapToTickEnabled = true,
                TickFrequency       = 1,
                Margin              = new Thickness(0, 0, 0, 4)
            };
            slider.ValueChanged += (s, e) => tvCount.Text = $"Flux simultanes : {(int)slider.Value}";
            layout.Children.Add(slider);
            layout.Children.Add(new TextBlock
            {
                Text         = "1 flux = stable  |  2-3 flux = debit x N  |  4 flux = max",
                Foreground   = hintBrush,
                FontSize     = 11,
                Margin       = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            // ── Boutons Annuler / Sauvegarder ────────────────────────────────
            var buttonsPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(20, 12, 20, 16)
            };
            var btnCancel = new Button
            {
                Content = "Annuler",
                Margin  = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 6, 12, 6)
            };
            var btnSave = new Button
            {
                Content    = "Sauvegarder",
                Padding    = new Thickness(12, 6, 12, 6),
                Background = accentBrush,
                Foreground = Brushes.White
            };

            btnCancel.Click += (s, e) => window.Close();
            btnSave.Click   += (s, e) =>
            {
                var updated = new XrayDnsProfile
                {
                    Id            = p.Id,
                    ProfileName   = string.IsNullOrWhiteSpace(etName.Text) ? "Profil" : etName.Text,
                    XrayLink      = etLink.Text.Trim(),
                    Protocol      = string.IsNullOrWhiteSpace(etProto.Text) ? "vmess" : etProto.Text.ToLower().Trim(),
                    ServerAddress = etHost.Text.Trim(),
                    ServerPort    = int.TryParse(etPort.Text, out var sp) ? sp : 443,
                    Uuid          = etUuid.Text.Trim(),
                    Transport     = string.IsNullOrWhiteSpace(etTrans.Text) ? "ws" : etTrans.Text.ToLower().Trim(),
                    WsPath        = string.IsNullOrWhiteSpace(etPath.Text) ? "/" : etPath.Text.Trim(),
                    WsHost        = etWsHost.Text.Trim(),
                    Tls           = cbTls.IsChecked == true,
                    Sni           = etSni.Text.Trim(),
                    AllowInsecure = cbInsec.IsChecked == true,
                    DnsServer     = string.IsNullOrWhiteSpace(etDns.Text) ? "8.8.8.8" : etDns.Text.Trim(),
                    Nameserver    = etNs.Text.Trim(),
                    PublicKey     = etPubKey.Text.Trim(),
                    TunnelCount   = (int)slider.Value,
                    IsSelected    = p.IsSelected
                };
                onSave(updated);
                window.Close();
            };

            buttonsPanel.Children.Add(btnCancel);
            buttonsPanel.Children.Add(btnSave);
            DockPanel.SetDock(buttonsPanel, Dock.Bottom);
            outer.Children.Add(buttonsPanel);
            outer.Children.Add(scroll);
            window.Content = outer;
            window.ShowDialog();
        }
    }
}
