using Newtonsoft.Json;

namespace KighmuVpnWindows.Models
{
    /// <summary>Équivalent exact de SlowDnsConfig (data class dans TunnelConfig.kt)</summary>
    public class SlowDnsConfig
    {
        [JsonProperty("dnsServer")] public string DnsServer { get; set; } = "8.8.8.8";
        [JsonProperty("dnsPort")] public int DnsPort { get; set; } = 53;
        [JsonProperty("nameserver")] public string Nameserver { get; set; } = "";
        [JsonProperty("publicKey")] public string PublicKey { get; set; } = "";
        [JsonProperty("privateKey")] public string PrivateKey { get; set; } = "";
        [JsonProperty("dnsPayload")] public string DnsPayload { get; set; } = "";
        [JsonProperty("useUdp")] public bool UseUdp { get; set; } = true;
        [JsonProperty("sshHost")] public string SshHost { get; set; } = "";
        [JsonProperty("sshPort")] public int SshPort { get; set; } = 22;
        [JsonProperty("sshUser")] public string SshUser { get; set; } = "";
        [JsonProperty("sshPass")] public string SshPass { get; set; } = "";
        [JsonProperty("proxyHost")] public string ProxyHost { get; set; } = "127.0.0.1";
        [JsonProperty("proxyPort")] public int ProxyPort { get; set; } = 22;

        public SlowDnsConfig Clone() => (SlowDnsConfig)MemberwiseClone();
    }
}
