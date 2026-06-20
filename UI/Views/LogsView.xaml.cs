using KighmuVpnWindows.Utils;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView()
        {
            InitializeComponent();
            LoadHistory();

            KighmuLogger.OnLogLine += OnLogLine;
            Unloaded += LogsView_Unloaded;
        }

        private void LoadHistory()
        {
            var lines = KighmuLogger.GetRecentLines(200);
            LogOutput.Text = lines.Length > 0
                ? string.Join("\n", lines)
                : "En attente de connexion...";
            ScrollToEnd();
        }

        private void OnLogLine(string line)
        {
            Dispatcher.Invoke(() =>
            {
                LogOutput.Text += "\n" + line;
                ScrollToEnd();
            });
        }

        private void ScrollToEnd()
        {
            LogScroll.ScrollToEnd();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            LogOutput.Text = "";
        }

        // Evite la fuite memoire : chaque navigation vers Logs cree une nouvelle
        // instance de LogsView. Sans ce desabonnement, l'ancienne instance reste
        // accrochee a l'evenement OnLogLine indefiniment.
        private void LogsView_Unloaded(object sender, RoutedEventArgs e)
        {
            KighmuLogger.OnLogLine -= OnLogLine;
            Unloaded -= LogsView_Unloaded;
        }
    }
}
