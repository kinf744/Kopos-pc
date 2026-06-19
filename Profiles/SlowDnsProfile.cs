using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de SlowDnsProfile.kt</summary>
    public class SlowDnsProfile
    {
        [JsonProperty("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")] public string ProfileName { get; set; } = "";

        // SSH
        [JsonProperty("sshHost")] public string SshHost { get; set; } = "";
        [JsonProperty("sshPort")] public int SshPort { get; set; } = 22;
        [JsonProperty("sshUser")] public string SshUser { get; set; } = "";
        [JsonProperty("sshPass")] public string SshPass { get; set; } = "";

        // SlowDNS
        [JsonProperty("dnsServer")] public string DnsServer { get; set; } = "8.8.8.8";
        [JsonProperty("nameserver")] public string Nameserver { get; set; } = "";
        [JsonProperty("publicKey")] public string PublicKey { get; set; } = "";

        // HTTP CONNECT proxy (fallback transport)
        [JsonProperty("proxyHost")] public string ProxyHost { get; set; } = "127.0.0.1";
        [JsonProperty("proxyPort")] public int ProxyPort { get; set; } = 22;
        [JsonProperty("customPayload")] public string CustomPayload { get; set; } = "";

        // Tunnels parallèles
        [JsonProperty("tunnelCount")] public int TunnelCount { get; set; } = 1;

        [JsonProperty("isSelected")] public bool IsSelected { get; set; } = false;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static SlowDnsProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<SlowDnsProfile>(json) ?? new SlowDnsProfile();

        public static List<SlowDnsProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<SlowDnsProfile>>(json) ?? new List<SlowDnsProfile>();

        public static string ListToJson(List<SlowDnsProfile> list) => JsonConvert.SerializeObject(list);
    }
}
