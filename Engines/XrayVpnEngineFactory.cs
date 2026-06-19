using KighmuVpnWindows.Profiles;
using KighmuVpnWindows.Utils;
using System.Linq;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Equivalent de XrayVpnEngineFactory.kt.
    /// Prend le PREMIER profil selectionne uniquement - pas de multi.
    /// </summary>
    public static class XrayVpnEngineFactory
    {
        private const string TAG = "XrayVpnEngineFactory";

        public static XrayVpnEngine Create()
        {
            var repo     = new XrayVpnProfileRepository();
            var selected = repo.GetSelected();

            // Premier profil selectionne, sinon premier de la liste, sinon profil par defaut
            var profile = selected.FirstOrDefault()
                       ?? repo.GetAll().FirstOrDefault()
                       ?? BuildDefaultProfile();

            KighmuLogger.Info(TAG, $"Profil charge: {profile.ProfileName} (mode={profile.ActiveMode})");
            return new XrayVpnEngine(profile, instanceId: 0);
        }

        private static XrayVpnProfile BuildDefaultProfile() => new XrayVpnProfile
        {
            ProfileName   = "Default",
            ActiveMode    = "json",
            XrayJson      = XrayVpnProfile.DEFAULT_JSON,
            Protocol      = "vmess",
            ServerAddress = "",
            ServerPort    = 443
        };
    }
}
