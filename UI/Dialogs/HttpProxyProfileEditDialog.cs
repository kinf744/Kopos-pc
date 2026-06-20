using KighmuVpnWindows.Profiles;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows.UI.Dialogs
{
    /// <summary>Equivalent de HttpProxyProfileEditDialog.kt</summary>
    public static class HttpProxyProfileEditDialog
    {
        public static void Show(Window? owner, HttpProxyProfile? profile, Action<HttpProxyProfile> onSave)
        {
            bool isEdit = profile != null;
            var p = profile ?? new HttpProxyProfile();

            var textBrush   = (Brush)Application.Current.TryFindResource("TextPrimaryBrush")   ?? Brushes.White;
            var hintBrush   = (Brush)Application.Current.TryFindResource("TextHintBrush")       ?? Brushes.Gray;
            var accentBrush = (Brush)Application.Current.TryFindResource("AccentBlueBrush")     ?? Brushes.DodgerBlue;
            var fieldBg     = (Brush)Application.Current.TryFindResource("SurfaceCardBrush")    ?? Brushes.DarkSlateGray;
            var bgBrush     = (Brush)Application.Current.TryFindResource("BackgroundDarkBrush") ?? Brushes.Black;

            var window = new Window
            {
                Title  = isEdit ? "Modifier profil HTTP Proxy" : "Nouveau profil HTTP Proxy",
                Width  = 420,
                Height = 640,
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
                    Height          = multiline ? 80 : double.NaN,
                    VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
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

            Label("HTTP PROXY");
            var etProxyHost = Field("Proxy Host", p.ProxyHost);
            var etProxyPort = Field("Proxy Port", p.ProxyPort.ToString());

            Label("PAYLOAD");
            layout.Children.Add(new TextBlock
            {
                Text         = "Variables: [host], [crlf], [cr], [lf]",
                Foreground   = hintBrush,
                FontSize     = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 4)
            });
            var etPayload = Field("Custom Payload", p.CustomPayload, multiline: true);

            // Boutons
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
                var updated = new HttpProxyProfile
                {
                    Id            = p.Id,
                    ProfileName   = string.IsNullOrWhiteSpace(etName.Text) ? "Profil" : etName.Text,
                    SshHost       = etSshHost.Text.Trim(),
                    SshPort       = int.TryParse(etSshPort.Text, out var sp) ? sp : 22,
                    SshUser       = etSshUser.Text.Trim(),
                    SshPass       = etSshPass.Text,
                    ProxyHost     = etProxyHost.Text.Trim(),
                    ProxyPort     = int.TryParse(etProxyPort.Text, out var pp) ? pp : 8080,
                    CustomPayload = etPayload.Text,
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
