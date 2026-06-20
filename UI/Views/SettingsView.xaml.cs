using KighmuVpnWindows.Config;
using Microsoft.Win32;
using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class SettingsView : UserControl
    {
        private const string PREFS_NAME    = "settings";
        private const string KEY_AUTOSTART = "auto_start";
        private const string KEY_TRAY      = "minimize_to_tray";

        private readonly LocalStorage _prefs = new LocalStorage(PREFS_NAME);
        private bool _loading = true;

        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();
            _loading = false;

            AutoStartCheck.Checked   += AutoStart_Changed;
            AutoStartCheck.Unchecked += AutoStart_Changed;
            MinimizeToTrayCheck.Checked   += Tray_Changed;
            MinimizeToTrayCheck.Unchecked += Tray_Changed;
        }

        private void LoadSettings()
        {
            AutoStartCheck.IsChecked      = _prefs.GetString(KEY_AUTOSTART, "false") == "true";
            MinimizeToTrayCheck.IsChecked = _prefs.GetString(KEY_TRAY,      "false") == "true";
        }

        private void AutoStart_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_loading) return;
            bool enabled = AutoStartCheck.IsChecked == true;
            _prefs.SetString(KEY_AUTOSTART, enabled.ToString().ToLower());
            SetWindowsAutoStart(enabled);
        }

        private void Tray_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_loading) return;
            bool enabled = MinimizeToTrayCheck.IsChecked == true;
            _prefs.SetString(KEY_TRAY, enabled.ToString().ToLower());
        }

        private static void SetWindowsAutoStart(bool enable)
        {
            const string RUN_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string APP_NAME = "KighmuVPN";
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, writable: true);
            if (key == null) return;
            if (enable)
            {
                string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null) key.SetValue(APP_NAME, exePath);
            }
            else
            {
                key.DeleteValue(APP_NAME, throwOnMissingValue: false);
            }
        }
    }
}
