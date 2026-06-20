using KighmuVpnWindows.UI.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KighmuVpnWindows
{
    public partial class MainWindow : Window
    {
        private Button? _activeNavButton;

        public MainWindow()
        {
            InitializeComponent();
            NavigateTo(new HomeView(), NavHome);
        }

        private void NavigateTo(UserControl view, Button activeBtn)
        {
            MainContent.Content = view;
            UpdateNavButtons(activeBtn);
        }

        private void UpdateNavButtons(Button active)
        {
            var activeBrush   = (Brush)FindResource("NavActiveBrush");
            var inactiveBrush = (Brush)FindResource("NavInactiveBrush");

            foreach (var btn in new[] { NavHome, NavConfig, NavLogs, NavSettings })
                btn.Foreground = (btn == active) ? activeBrush : inactiveBrush;

            _activeNavButton = active;
        }

        private void NavHome_Click(object sender, RoutedEventArgs e)     => NavigateTo(new HomeView(),     NavHome);
        private void NavConfig_Click(object sender, RoutedEventArgs e)   => NavigateTo(new ConfigView(),   NavConfig);
        private void NavLogs_Click(object sender, RoutedEventArgs e)     => NavigateTo(new LogsView(),     NavLogs);
        private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(new SettingsView(), NavSettings);
    }
}
