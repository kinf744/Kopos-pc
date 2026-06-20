using KighmuVpnWindows.Utils;
using System;
using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView()
        {
            InitializeComponent();
            KighmuLogger.OnLogLine += OnLogLine;
        }

        private void OnLogLine(string line)
        {
            Dispatcher.Invoke(() =>
            {
                LogOutput.Text += "\n" + line;
            });
        }
    }
}
