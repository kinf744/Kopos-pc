using System.Windows.Controls;

namespace KighmuVpnWindows.UI.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView()
        {
            InitializeComponent();
        }

        // TODO: brancher sur Utils/KighmuLogger.cs pour afficher les logs en temps réel
        public void AppendLog(string line)
        {
            LogOutput.Text += "\n" + line;
        }
    }
}
