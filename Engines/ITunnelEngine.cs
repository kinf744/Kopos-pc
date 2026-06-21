using System.Threading.Tasks;

namespace KighmuVpnWindows.Engines
{
    /// <summary>
    /// Équivalent exact de TunnelEngine.kt (interface).
    /// Chaque mode implémente ceci pour démarrer/arrêter son transport.
    /// Retourne le port local SOCKS5/HTTP par lequel l'interface TUN route le trafic.
    /// </summary>
    public interface ITunnelEngine
    {
        /// <summary>Démarre l'engine. Retourne le port proxy local.</summary>
        Task<int> Start();

        /// <summary>Arrête l'engine et nettoie les ressources.</summary>
        Task Stop();

        /// <summary>Le tunnel est-il actuellement actif ?</summary>
        bool IsRunning();

        /// <summary>
        /// IP reelle du serveur VPN distant (resolue via DNS au demarrage).
        /// Utilisee par RouteManager pour exclure cette IP du tunnel et
        /// eviter une boucle de routage (equivalent addSplitRoutes cote Android).
        /// </summary>
        string? ServerIp { get; }

        /// <summary>
        /// Démarre le routage tun2socks vers l'adaptateur Wintun nommé `tunAdapterName`.
        /// Équivalent de startTun2Socks(fd: Int) côté Android, mais ici on passe
        /// le nom de l'adaptateur réseau Wintun plutôt qu'un file descriptor.
        /// </summary>
        void StartTun2Socks(string tunAdapterName);
    }
}
