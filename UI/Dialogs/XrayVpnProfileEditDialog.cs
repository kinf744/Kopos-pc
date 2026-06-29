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

            // ── Options TLS ───────────────────────────────────────────────────
            Label("OPTIONS TLS");
            var cbAllowInsecure = CheckField("Ignorer erreurs certificat TLS (allowInsecure)", p.AllowInsecure);

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
            var etLink = new TextBox
            {
                Text            = p.XrayLink,
                Foreground      = textBrush,
                Background      = fieldBg,
                Padding         = new Thickness(8),
                BorderThickness = new Thickness(0)
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
                var tmp = new XrayVpnProfile();
                string xrayLink = linkMode ? etLink.Text.Trim() : p.XrayLink;
                if (linkMode && !string.IsNullOrWhiteSpace(xrayLink))
                    XrayVpnProfile.ParseLinkIntoProfile(xrayLink, tmp);

                var updated = new XrayVpnProfile
                {
                    Id            = p.Id,
                    ProfileName   = string.IsNullOrWhiteSpace(etName.Text) ? "Profil" : etName.Text,
                    ActiveMode    = linkMode ? "link" : "json",
                    XrayLink      = xrayLink,
                    XrayJson      = linkMode ? p.XrayJson : etJson.Text.Trim(),
                    Protocol      = string.IsNullOrWhiteSpace(tmp.Protocol) ? "vmess" : tmp.Protocol,
                    ServerAddress = tmp.ServerAddress,
                    ServerPort    = tmp.ServerPort > 0 ? tmp.ServerPort : 443,
                    Uuid          = tmp.Uuid,
                    Transport     = string.IsNullOrWhiteSpace(tmp.Transport) ? "ws" : tmp.Transport,
                    WsPath        = string.IsNullOrWhiteSpace(tmp.WsPath) ? "/" : tmp.WsPath,
                    WsHost        = tmp.WsHost,
                    Tls           = tmp.Tls,
                    Sni           = tmp.Sni,
                    Fingerprint   = string.IsNullOrWhiteSpace(tmp.Fingerprint) ? "chrome" : tmp.Fingerprint,
                    AllowInsecure = cbAllowInsecure.IsChecked == true || tmp.AllowInsecure,
                    PublicKey     = tmp.PublicKey,
                    ShortId       = tmp.ShortId,
                    Flow          = tmp.Flow,
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
