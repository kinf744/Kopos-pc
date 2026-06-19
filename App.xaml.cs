using System.Windows;

namespace KighmuVpnWindows
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // TODO: vérifier ici si Wintun.dll est présent, sinon avertir l'utilisateur
        }
    }
}
