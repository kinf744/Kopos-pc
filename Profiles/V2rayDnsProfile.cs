using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>
    /// Équivalent exact de V2rayDnsProfile.kt.
    /// NOTE: structure identique à XrayDnsProfile.cs côté Kotlin source -
    /// conservé séparément pour fidélité, à fusionner plus tard si confirmé obsolète.
    /// </summary>
    public class V2rayDnsProfile
    {
        [JsonProperty("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")] public string ProfileName { get; set; } = "";

        // Xray/V2Ray config
        [JsonProperty("xrayLink")] public string XrayLink { get; set; } = "";
        [JsonProperty("xrayJsonConfig")] public string XrayJsonConfig { get; set; } = "";
        [JsonProperty("protocol")] public string Protocol { get; set; } = "vmess";
        [JsonProperty("serverAddress")] public string ServerAddress { get; set; } = "";
        [JsonProperty("serverPort")] public int ServerPort { get; set; } = 443;
        [JsonProperty("uuid")] public string Uuid { get; set; } = "";
        [JsonProperty("encryption")] public string Encryption { get; set; } = "auto";
        [JsonProperty("transport")] public string Transport { get; set; } = "ws";
        [JsonProperty("wsPath")] public string WsPath { get; set; } = "/";
        [JsonProperty("wsHost")] public string WsHost { get; set; } = "";
        [JsonProperty("tls")] public bool Tls { get; set; } = true;
        [JsonProperty("sni")] public string Sni { get; set; } = "";
        [JsonProperty("allowInsecure")] public bool AllowInsecure { get; set; } = false;

        // SlowDNS config
        [JsonProperty("dnsServer")] public string DnsServer { get; set; } = "8.8.8.8";
        [JsonProperty("dnsPort")] public int DnsPort { get; set; } = 53;
        [JsonProperty("nameserver")] public string Nameserver { get; set; } = "";
        [JsonProperty("publicKey")] public string PublicKey { get; set; } = "";

        // Tunnels parallèles
        [JsonProperty("tunnelCount")] public int TunnelCount { get; set; } = 1;

        [JsonProperty("isSelected")] public bool IsSelected { get; set; } = false;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static V2rayDnsProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<V2rayDnsProfile>(json) ?? new V2rayDnsProfile();

        public static List<V2rayDnsProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<V2rayDnsProfile>>(json) ?? new List<V2rayDnsProfile>();

        public static string ListToJson(List<V2rayDnsProfile> list) => JsonConvert.SerializeObject(list);
    }
}
