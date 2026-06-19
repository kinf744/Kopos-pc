using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KighmuVpnWindows.UI.Views;

namespace KighmuVpnWindows
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            NavigateTo(new HomeView());
        }

        private void NavigateTo(UserControl view)
        {
            MainContent.Content = view;
        }

        private void NavHome_Click(object sender, RoutedEventArgs e) => NavigateTo(new HomeView());
        private void NavConfig_Click(object sender, RoutedEventArgs e) => NavigateTo(new ConfigView());
        private void NavLogs_Click(object sender, RoutedEventArgs e) => NavigateTo(new LogsView());
        private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(new SettingsView());
    }
}
