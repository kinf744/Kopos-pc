using KighmuVpnWindows.Config;
using Microsoft.Win32;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class SettingsView : UserControl
    {
        private const string PREFS_NAME = "settings";
        private readonly LocalStorage _prefs = new LocalStorage(PREFS_NAME);
        private bool _loading = true;

        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();
            _loading = false;
            SubscribeEvents();
        }

        private void SubscribeEvents()
        {
            AutoStartCheck.Checked       += OnSettingChanged;
            AutoStartCheck.Unchecked     += OnSettingChanged;
            MinimizeToTrayCheck.Checked  += OnSettingChanged;
            MinimizeToTrayCheck.Unchecked+= OnSettingChanged;
            AutoReconnectCheck.Checked   += OnSettingChanged;
            AutoReconnectCheck.Unchecked += OnSettingChanged;
            KillSwitchCheck.Checked      += OnSettingChanged;
            KillSwitchCheck.Unchecked    += OnSettingChanged;
            NotificationsCheck.Checked   += OnSettingChanged;
            NotificationsCheck.Unchecked += OnSettingChanged;
            WakelockCheck.Checked        += OnSettingChanged;
            WakelockCheck.Unchecked      += OnSettingChanged;
            KeepAliveCheck.Checked       += OnSettingChanged;
            KeepAliveCheck.Unchecked     += OnSettingChanged;
            CompressionCheck.Checked     += OnSettingChanged;
            CompressionCheck.Unchecked   += OnSettingChanged;
            HttpPingCheck.Checked        += OnSettingChanged;
            HttpPingCheck.Unchecked      += OnSettingChanged;
            DnsProtectionCheck.Checked   += OnSettingChanged;
            DnsProtectionCheck.Unchecked += OnSettingChanged;
            EnableDnsCheck.Checked       += OnSettingChanged;
            EnableDnsCheck.Unchecked     += OnSettingChanged;
            DnsForwardingCheck.Checked   += OnSettingChanged;
            DnsForwardingCheck.Unchecked += OnSettingChanged;
        }

        private void LoadSettings()
        {
            // Hardware ID
            TvHardwareId.Text = GetHardwareId();

            // General
            AutoStartCheck.IsChecked       = GetBool("auto_start",      false);
            MinimizeToTrayCheck.IsChecked  = GetBool("minimize_to_tray",false);

            // Connexion
            AutoReconnectCheck.IsChecked   = GetBool("auto_reconnect",  true);
            KillSwitchCheck.IsChecked      = GetBool("kill_switch",     false);
            NotificationsCheck.IsChecked   = GetBool("notifications",   true);
            WakelockCheck.IsChecked        = GetBool("wakelock",        false);
            KeepAliveCheck.IsChecked       = GetBool("keepalive",       false);
            CompressionCheck.IsChecked     = GetBool("compression",     false);
            HttpPingCheck.IsChecked        = GetBool("http_ping",       false);

            // DNS
            DnsProtectionCheck.IsChecked   = GetBool("dns_protection",  true);
            EnableDnsCheck.IsChecked       = GetBool("enable_dns",      false);
            DnsForwardingCheck.IsChecked   = GetBool("dns_forwarding",  false);
            DnsPrimaryBox.Text             = _prefs.GetString("dns_primary",   "8.8.8.8");
            DnsSecondaryBox.Text           = _prefs.GetString("dns_secondary", "8.8.4.4");

            // Avance
            MtuBox.Text   = _prefs.GetString("mtu",   "");
            UdpgwBox.Text = _prefs.GetString("udpgw", "");

            // Version
            TvAppVersion.Text = "KIGHMU VPN v1.0.0 (Windows)";
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (sender == AutoStartCheck)
                SetWindowsAutoStart(AutoStartCheck.IsChecked == true);
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            // General
            SetBool("auto_start",       AutoStartCheck.IsChecked      == true);
            SetBool("minimize_to_tray", MinimizeToTrayCheck.IsChecked == true);

            // Connexion
            SetBool("auto_reconnect",   AutoReconnectCheck.IsChecked  == true);
            SetBool("kill_switch",      KillSwitchCheck.IsChecked     == true);
            SetBool("notifications",    NotificationsCheck.IsChecked  == true);
            SetBool("wakelock",         WakelockCheck.IsChecked       == true);
            SetBool("keepalive",        KeepAliveCheck.IsChecked      == true);
            SetBool("compression",      CompressionCheck.IsChecked    == true);
            SetBool("http_ping",        HttpPingCheck.IsChecked       == true);

            // DNS
            SetBool("dns_protection",   DnsProtectionCheck.IsChecked  == true);
            SetBool("enable_dns",       EnableDnsCheck.IsChecked      == true);
            SetBool("dns_forwarding",   DnsForwardingCheck.IsChecked  == true);
            _prefs.SetString("dns_primary",   DnsPrimaryBox.Text.Trim());
            _prefs.SetString("dns_secondary", DnsSecondaryBox.Text.Trim());

            // Avance
            _prefs.SetString("mtu",   MtuBox.Text.Trim());
            _prefs.SetString("udpgw", UdpgwBox.Text.Trim());

            SetWindowsAutoStart(AutoStartCheck.IsChecked == true);

            MessageBox.Show("Reglages enregistres.", "Succes",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCopyHwid_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TvHardwareId.Text);
            MessageBox.Show("Hardware ID copie !", "Copie",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool GetBool(string key, bool defaultVal) =>
            _prefs.GetString(key, defaultVal ? "true" : "false") == "true";

        private void SetBool(string key, bool value) =>
            _prefs.SetString(key, value ? "true" : "false");

        private static string GetHardwareId()
        {
            try
            {
                string raw = Environment.MachineName
                           + Environment.UserName
                           + Environment.OSVersion.ToString();
                var bytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16).ToUpper();
            }
            catch { return "UNKNOWN"; }
        }

        private static void SetWindowsAutoStart(bool enable)
        {
            const string RUN_KEY  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string APP_NAME = "KighmuVPN";
            var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, writable: true);
            if (key == null) return;
            if (enable)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                if (exePath != null) key.SetValue(APP_NAME, exePath);
            }
            else
            {
                key.DeleteValue(APP_NAME, throwOnMissingValue: false);
            }
        }
    }
}
