namespace KighmuVpnWindows.Models
{
    /// <summary>
    /// Modes de tunnel (mêmes IDs que TunnelMode.kt pour compatibilité .kighmu).
    /// ZIVPN_UDP (id=7) retiré : pas de binaire Windows disponible.
    /// </summary>
    public enum TunnelMode
    {
        SLOW_DNS = 0,
        HTTP_PROXY = 1,
        SSH_SSL_TLS = 3,
        V2RAY_XRAY = 4,
        V2RAY_SLOWDNS = 5,
        HYSTERIA_UDP = 6
    }

    public static class TunnelModeExtensions
    {
        public static string Label(this TunnelMode mode) => mode switch
        {
            TunnelMode.SLOW_DNS => "SlowDNS",
            TunnelMode.HTTP_PROXY => "HTTP Proxy + Payload",
            TunnelMode.SSH_SSL_TLS => "SSH SSL/TLS",
            TunnelMode.V2RAY_XRAY => "V2Ray / Xray",
            TunnelMode.V2RAY_SLOWDNS => "V2Ray + SlowDNS",
            TunnelMode.HYSTERIA_UDP => "Hysteria UDP",
            _ => "Inconnu"
        };

        public static TunnelMode FromId(int id) =>
            System.Enum.IsDefined(typeof(TunnelMode), id) ? (TunnelMode)id : TunnelMode.HTTP_PROXY;
    }

    public enum ConnectionStatus
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED,
        RECONNECTING,
        STOPPING,
        ERROR
    }
}
