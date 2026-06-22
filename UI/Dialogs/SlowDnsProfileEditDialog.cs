using KighmuVpnWindows.Profiles;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows.UI.Dialogs
{
    /// <summary>Equivalent de ProfileEditDialog.kt (edition profil SlowDNS)</summary>
    public static class SlowDnsProfileEditDialog
    {
        public static void Show(Window? owner, SlowDnsProfile? profile, Action<SlowDnsProfile> onSave)
        {
            bool isEdit = profile != null;
            var p = profile ?? new SlowDnsProfile();

            var textBrush   = (Brush)Application.Current.TryFindResource("TextPrimaryBrush")   ?? Brushes.White;
            var hintBrush   = (Brush)Application.Current.TryFindResource("TextHintBrush")       ?? Brushes.Gray;
            var accentBrush = (Brush)Application.Current.TryFindResource("AccentBlueBrush")     ?? Brushes.DodgerBlue;
            var fieldBg     = (Brush)Application.Current.TryFindResource("SurfaceCardBrush")    ?? Brushes.DarkSlateGray;
            var bgBrush     = (Brush)Application.Current.TryFindResource("BackgroundDarkBrush") ?? Brushes.Black;

            var window = new Window
            {
                Title = isEdit ? "Modifier profil" : "Nouveau profil",
                Width = 420,
                Height = 640,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = bgBrush,
                ResizeMode = ResizeMode.NoResize
            };

            var outer = new DockPanel();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var layout = new StackPanel { Margin = new Thickness(20) };
            scroll.Content = layout;

            void Label(string text)
            {
                layout.Children.Add(new TextBlock
                {
                    Text = text,
                    Foreground = accentBrush,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 16, 0, 4)
                });
            }

            TextBox Field(string label, string value)
            {
                layout.Children.Add(new TextBlock
                {
                    Text = label,
                    Foreground = hintBrush,
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 2)
                });
                var tb = new TextBox
                {
                    Text = value,
                    Foreground = textBrush,
                    Background = fieldBg,
                    Padding = new Thickness(8),
                    BorderThickness = new Thickness(0)
                };
                layout.Children.Add(tb);
                return tb;
            }

            Label("PROFILE");
            var etName = Field("Profile Name", p.ProfileName);

            Label("SSH CONFIGURATION");
            var etSshHost = Field("SSH Host", p.SshHost);
            var etSshPort = Field("SSH Port", p.SshPort.ToString());
            var etSshUser = Field("Username", p.SshUser);
            var etSshPass = Field("Password", p.SshPass);

            Label("SLOWDNS CONFIGURATION");
            var etDns    = Field("DNS Server", p.DnsServer);
            var etDnsPort = Field("DNS Port", p.DnsPort.ToString());
            var etNs     = Field("Nameserver", p.Nameserver);
            var etPubKey = Field("Public Key", p.PublicKey);

            Label("HTTP CONNECT PROXY");
            var etProxyHost = Field("Proxy Host", p.ProxyHost);
            var etProxyPort = Field("Proxy Port", p.ProxyPort.ToString());
            var etPayload   = Field("Payload (optionnel)", p.CustomPayload);

            Label("TUNNELS PARALLELES");
            var tvTunnelCount = new TextBlock
            {
                Text = $"Flux simultanes : {p.TunnelCount}",
                Foreground = textBrush,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 4)
            };
            layout.Children.Add(tvTunnelCount);

            var sliderTunnel = new Slider
            {
                Minimum = 1,
                Maximum = 4,
                Value = Math.Min(Math.Max(p.TunnelCount, 1), 4),
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin = new Thickness(0, 0, 0, 4)
            };
            sliderTunnel.ValueChanged += (s, e) => tvTunnelCount.Text = $"Flux simultanes : {(int)sliderTunnel.Value}";
            layout.Children.Add(sliderTunnel);

            layout.Children.Add(new TextBlock
            {
                Text = "1 flux = stable  |  2-3 flux = debit x N  |  4 flux = max",
                Foreground = hintBrush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20, 12, 20, 16)
            };
            var btnCancel = new Button { Content = "Annuler", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
            var btnSave = new Button
            {
                Content = "Sauvegarder",
                Padding = new Thickness(12, 6, 12, 6),
                Background = accentBrush,
                Foreground = Brushes.White
            };
            btnCancel.Click += (s, e) => window.Close();
            btnSave.Click += (s, e) =>
            {
                var updated = new SlowDnsProfile
                {
                    Id            = p.Id,
                    ProfileName   = string.IsNullOrWhiteSpace(etName.Text) ? "Profil" : etName.Text,
                    SshHost       = etSshHost.Text,
                    SshPort       = int.TryParse(etSshPort.Text, out var sp) ? sp : 22,
                    SshUser       = etSshUser.Text,
                    SshPass       = etSshPass.Text,
                    DnsServer     = string.IsNullOrWhiteSpace(etDns.Text) ? "8.8.8.8" : etDns.Text,
                    DnsPort       = int.TryParse(etDnsPort.Text, out var dp) ? dp : 53,
                    Nameserver    = etNs.Text,
                    PublicKey     = etPubKey.Text,
                    ProxyHost     = etProxyHost.Text,
                    ProxyPort     = int.TryParse(etProxyPort.Text, out var pp) ? pp : 8080,
                    CustomPayload = etPayload.Text,
                    TunnelCount   = (int)sliderTunnel.Value,
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
