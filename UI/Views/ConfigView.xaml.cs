using System.Windows;
using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class ConfigView : UserControl
    {
        public ConfigView()
        {
            InitializeComponent();
        }

        private void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // TODO: vider DynamicFieldsPanel et injecter les bons champs
            // selon Models/TunnelConfig.cs -> TunnelMode sélectionné
            DynamicFieldsPanel.Children.Clear();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            // TODO: équivalent ImportActivity.kt
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            // TODO: équivalent ExportActivity.kt
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // TODO: sauvegarder via Profiles/ProfileRepository.cs
        }
    }
}
