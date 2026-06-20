using KighmuVpnWindows.Profiles;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows.UI.Dialogs
{
    /// <summary>Equivalent de HysteriaProfileEditDialog.kt</summary>
    public static class HysteriaProfileEditDialog
    {
        public static void Show(Window? owner, HysteriaProfile? profile, Action<HysteriaProfile> onSave)
        {
            bool isEdit = profile != null;
            var p = profile ?? new HysteriaProfile();

            var textBrush   = (Brush)Application.Current.TryFindResource("TextPrimaryBrush")   ?? Brushes.White;
            var hintBrush   = (Brush)Application.Current.TryFindResource("TextHintBrush")       ?? Brushes.Gray;
            var accentBrush = (Brush)Application.Current.TryFindResource("AccentBlueBrush")     ?? Brushes.DodgerBlue;
            var fieldBg     = (Brush)Application.Current.TryFindResource("SurfaceCardBrush")    ?? Brushes.DarkSlateGray;
            var bgBrush     = (Brush)Application.Current.TryFindResource("BackgroundDarkBrush") ?? Brushes.Black;

            var window = new Window
            {
                Title  = isEdit ? "Modifier profil Hysteria" : "Nouveau profil Hysteria",
                Width  = 420,
                Height = 580,
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

            Label("PROFILE");
            var etName = Field("Profile Name", p.ProfileName);

            Label("SERVEUR");
            var etHost = Field("Server Address", p.ServerAddress);
            var etPort = Field("Server Port", p.ServerPort.ToString());
            var etSni  = Field("SNI (optionnel)", p.Sni);

            Label("AUTHENTIFICATION");
            var etAuth = Field("Auth Password", p.AuthPassword);
            var etObfs = Field("Obfs Password (optionnel)", p.ObfsPassword);

            Label("DEBIT");
            var etUp   = Field("Upload Mbps", p.UploadMbps.ToString());
            var etDown = Field("Download Mbps", p.DownloadMbps.ToString());

            Label("PORT HOPPING");
            var etHop  = Field("Plage de ports (ex: 20000-50000)", p.PortHopping);

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
                var updated = new HysteriaProfile
                {
                    Id            = p.Id,
                    ProfileName   = string.IsNullOrWhiteSpace(etName.Text) ? "Profil" : etName.Text,
                    ServerAddress = etHost.Text,
                    ServerPort    = int.TryParse(etPort.Text, out var sp) ? sp : 36712,
                    Sni           = etSni.Text,
                    AuthPassword  = etAuth.Text,
                    ObfsPassword  = etObfs.Text,
                    UploadMbps    = int.TryParse(etUp.Text,   out var up)   ? up   : 100,
                    DownloadMbps  = int.TryParse(etDown.Text, out var down) ? down : 100,
                    PortHopping   = string.IsNullOrWhiteSpace(etHop.Text) ? "20000-50000" : etHop.Text,
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
