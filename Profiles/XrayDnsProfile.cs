using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Equivalent exact de XrayDnsProfile.kt</summary>
    public class XrayDnsProfile
    {
        [JsonProperty("id")]          public string Id          { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")] public string ProfileName { get; set; } = "";

        // Xray/V2Ray config
        [JsonProperty("xrayLink")]       public string XrayLink       { get; set; } = "";
        [JsonProperty("xrayJsonConfig")] public string XrayJsonConfig { get; set; } = "";
        [JsonProperty("protocol")]       public string Protocol       { get; set; } = "vmess";
        [JsonProperty("serverAddress")]  public string ServerAddress  { get; set; } = "";
        [JsonProperty("serverPort")]     public int    ServerPort     { get; set; } = 443;
        [JsonProperty("uuid")]           public string Uuid           { get; set; } = "";
        [JsonProperty("encryption")]     public string Encryption     { get; set; } = "auto";
        [JsonProperty("transport")]      public string Transport      { get; set; } = "ws";
        [JsonProperty("wsPath")]         public string WsPath         { get; set; } = "/";
        [JsonProperty("wsHost")]         public string WsHost         { get; set; } = "";
        [JsonProperty("tls")]            public bool   Tls            { get; set; } = true;
        [JsonProperty("sni")]            public string Sni            { get; set; } = "";
        [JsonProperty("allowInsecure")]  public bool   AllowInsecure  { get; set; } = false;

        // SlowDNS config
        [JsonProperty("dnsServer")]  public string DnsServer  { get; set; } = "8.8.8.8";
        [JsonProperty("dnsPort")]    public int    DnsPort    { get; set; } = 53;
        [JsonProperty("nameserver")] public string Nameserver { get; set; } = "";
        [JsonProperty("publicKey")]  public string PublicKey  { get; set; } = "";

        // Tunnels paralleles
        [JsonProperty("tunnelCount")] public int TunnelCount { get; set; } = 1;

        // Etat
        [JsonProperty("isSelected")] public bool IsSelected { get; set; } = false;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static XrayDnsProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<XrayDnsProfile>(json) ?? new XrayDnsProfile();

        public static List<XrayDnsProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<XrayDnsProfile>>(json) ?? new List<XrayDnsProfile>();

        public static string ListToJson(List<XrayDnsProfile> list) =>
            JsonConvert.SerializeObject(list);
    }
}
