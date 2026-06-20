using KighmuVpnWindows.Profiles;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows.UI.Dialogs
{
    /// <summary>Equivalent de XrayVpnProfileEditDialog.kt — mode lien OU mode JSON</summary>
    public static class XrayVpnProfileEditDialog
    {
        public static void Show(Window? owner, XrayVpnProfile? profile, Action<XrayVpnProfile> onSave)
        {
            bool isEdit = profile != null;
            var p = profile ?? new XrayVpnProfile();

            var textBrush   = (Brush)Application.Current.TryFindResource("TextPrimaryBrush")   ?? Brushes.White;
            var hintBrush   = (Brush)Application.Current.TryFindResource("TextHintBrush")       ?? Brushes.Gray;
            var accentBrush = (Brush)Application.Current.TryFindResource("AccentBlueBrush")     ?? Brushes.DodgerBlue;
            var dimBrush    = (Brush)Application.Current.TryFindResource("SurfaceCardBrush")    ?? Brushes.DimGray;
            var fieldBg     = (Brush)Application.Current.TryFindResource("SurfaceCardBrush")    ?? Brushes.DarkSlateGray;
            var bgBrush     = (Brush)Application.Current.TryFindResource("BackgroundDarkBrush") ?? Brushes.Black;

            var window = new Window
            {
                Title  = isEdit ? "Modifier profil V2Ray/Xray" : "Nouveau profil V2Ray/Xray",
                Width  = 480,
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

            TextBox Field(string label, string value, bool multiline = false)
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
                    BorderThickness = new Thickness(0),
                    TextWrapping    = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    AcceptsReturn   = multiline,
                    Height          = multiline ? 120 : double.NaN,
                    VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
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

            // ── SELECTEUR DE MODE ─────────────────────────────────────────────
            Label("MODE DE CONFIGURATION");
            bool startInLinkMode = p.ActiveMode != "json";

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            var rbLink = new RadioButton
            {
                Content    = "  Mode Lien  (vmess:// / vless:// / trojan://)",
                IsChecked  = startInLinkMode,
                Foreground = textBrush,
                Margin     = new Thickness(0, 0, 16, 0)
            };
            var rbJson = new RadioButton
            {
                Content   = "  Mode JSON direct",
                IsChecked = !startInLinkMode,
                Foreground = textBrush
            };
            modeRow.Children.Add(rbLink);
            modeRow.Children.Add(rbJson);
            layout.Children.Add(modeRow);

            // ── PANNEAU MODE LIEN ─────────────────────────────────────────────
            var panelLink = new StackPanel { Visibility = startInLinkMode ? Visibility.Visible : Visibility.Collapsed };

            // Champ lien + bouton Parser
            panelLink.Children.Add(new TextBlock
            {
                Text         = "Lien (vmess:// / vless:// / trojan://)",
                Foreground   = hintBrush,
                FontSize     = 10,
                Margin       = new Thickness(0, 4, 0, 2)
            });
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
            panelLink.Children.Add(linkRow);

            // Champs parsés (lecture après Parser)
            TextBox LinkField(string label, string value)
            {
                panelLink.Children.Add(new TextBlock { Text = label, Foreground = hintBrush, FontSize = 10, Margin = new Thickness(0, 4, 0, 2) });
                var tb = new TextBox { Text = value, Foreground = textBrush, Background = fieldBg, Padding = new Thickness(8), BorderThickness = new Thickness(0) };
                panelLink.Children.Add(tb);
                return tb;
            }
            CheckBox LinkCheck(string label, bool value)
            {
                var cb = new CheckBox { Content = label, IsChecked = value, Foreground = textBrush, Margin = new Thickness(0, 6, 0, 2) };
                panelLink.Children.Add(cb);
                return cb;
            }

            panelLink.Children.Add(new TextBlock { Text = "Champs remplis automatiquement apres parsing :", Foreground = hintBrush, FontSize = 10, Margin = new Thickness(0, 10, 0, 4) });
            var etProto  = LinkField("Protocol", p.Protocol);
            var etHost   = LinkField("Server Address", p.ServerAddress);
            var etPort   = LinkField("Server Port", p.ServerPort.ToString());
            var etUuid   = LinkField("UUID / Password", p.Uuid);
            var etTrans  = LinkField("Transport", p.Transport);
            var etPath   = LinkField("WS Path / gRPC Service", p.WsPath);
            var etWsHost = LinkField("WS Host", p.WsHost);
            var cbTls    = LinkCheck("TLS active", p.Tls);
            var etSni    = LinkField("SNI", p.Sni);
            var etFp     = LinkField("Fingerprint", p.Fingerprint);
            var cbInsec  = LinkCheck("Allow Insecure", p.AllowInsecure);
            var etPbk    = LinkField("Reality Public Key", p.PublicKey);
            var etSid    = LinkField("Reality Short ID", p.ShortId);
            var etFlow   = LinkField("VLESS Flow (ex: xtls-rprx-vision)", p.Flow);

            // Action Parser
            btnParse.Click += (s, e) =>
            {
                var link = etLink.Text.Trim();
                if (string.IsNullOrWhiteSpace(link)) return;
                var tmp = new XrayVpnProfile();
                XrayVpnProfile.ParseLinkIntoProfile(link, tmp);
                etProto.Text      = tmp.Protocol;
                etHost.Text       = tmp.ServerAddress;
                etPort.Text       = tmp.ServerPort.ToString();
                etUuid.Text       = tmp.Uuid;
                etTrans.Text      = tmp.Transport;
                etPath.Text       = tmp.WsPath;
                etWsHost.Text     = tmp.WsHost;
                cbTls.IsChecked   = tmp.Tls;
                etSni.Text        = tmp.Sni;
                etFp.Text         = tmp.Fingerprint;
                cbInsec.IsChecked = tmp.AllowInsecure;
                etPbk.Text        = tmp.PublicKey;
                etSid.Text        = tmp.ShortId;
                etFlow.Text       = tmp.Flow;
            };

            layout.Children.Add(panelLink);

            // ── PANNEAU MODE JSON ─────────────────────────────────────────────
            var panelJson = new StackPanel { Visibility = !startInLinkMode ? Visibility.Visible : Visibility.Collapsed };
            panelJson.Children.Add(new TextBlock
            {
                Text         = "Coller ici le JSON Xray complet",
                Foreground   = hintBrush,
                FontSize     = 10,
                Margin       = new Thickness(0, 4, 0, 2)
            });
            var etJson = new TextBox
            {
                Text            = string.IsNullOrWhiteSpace(p.XrayJson) ? XrayVpnProfile.DEFAULT_JSON : p.XrayJson,
                Foreground      = textBrush,
                Background      = fieldBg,
                Padding         = new Thickness(8),
                BorderThickness = new Thickness(0),
                TextWrapping    = TextWrapping.Wrap,
                AcceptsReturn   = true,
                Height          = 220,
                FontFamily      = new FontFamily("Consolas"),
                FontSize        = 11,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            panelJson.Children.Add(etJson);
            layout.Children.Add(panelJson);

            // Basculement mode
            rbLink.Checked += (s, e) =>
            {
                panelLink.Visibility = Visibility.Visible;
                panelJson.Visibility = Visibility.Collapsed;
                window.Height = 720;
            };
            rbJson.Checked += (s, e) =>
            {
                panelLink.Visibility = Visibility.Collapsed;
                panelJson.Visibility = Visibility.Visible;
                window.Height = 480;
            };

            // ── Boutons ───────────────────────────────────────────────────────
            var buttonsPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(20, 12, 20, 16)
            };
            var btnCancel = new Button { Content = "Annuler", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
            var btnSave   = new Button { Content = "Sauvegarder", Padding = new Thickness(12, 6, 12, 6), Background = accentBrush, Foreground = Brushes.White };

            btnCancel.Click += (s, e) => window.Close();
            btnSave.Click   += (s, e) =>
            {
                bool linkMode = rbLink.IsChecked == true;
                var updated = new XrayVpnProfile
                {
                    Id            = p.Id,
                    ProfileName   = string.IsNullOrWhiteSpace(etName.Text) ? "Profil" : etName.Text,
                    ActiveMode    = linkMode ? "link" : "json",
                    XrayLink      = linkMode ? etLink.Text.Trim() : p.XrayLink,
                    XrayJson      = linkMode ? p.XrayJson : etJson.Text.Trim(),
                    Protocol      = etProto.Text.ToLower().Trim(),
                    ServerAddress = etHost.Text.Trim(),
                    ServerPort    = int.TryParse(etPort.Text, out var sp) ? sp : 443,
                    Uuid          = etUuid.Text.Trim(),
                    Transport     = etTrans.Text.ToLower().Trim(),
                    WsPath        = string.IsNullOrWhiteSpace(etPath.Text) ? "/" : etPath.Text.Trim(),
                    WsHost        = etWsHost.Text.Trim(),
                    Tls           = cbTls.IsChecked == true,
                    Sni           = etSni.Text.Trim(),
                    Fingerprint   = string.IsNullOrWhiteSpace(etFp.Text) ? "chrome" : etFp.Text.Trim(),
                    AllowInsecure = cbInsec.IsChecked == true,
                    PublicKey     = etPbk.Text.Trim(),
                    ShortId       = etSid.Text.Trim(),
                    Flow          = etFlow.Text.Trim(),
                    IsSelected    = p.IsSelected
                };
                // Re-parser le lien pour regenerer XrayLinkJson
                if (linkMode && !string.IsNullOrWhiteSpace(updated.XrayLink))
                    XrayVpnProfile.ParseLinkIntoProfile(updated.XrayLink, updated);
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
