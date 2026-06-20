using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

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

        public static void ParseLinkIntoProfile(string link, XrayDnsProfile p)
        {
            try
            {
                if (link.StartsWith("vmess://"))
                {
                    var b64 = link.Substring("vmess://".Length);
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    var obj  = Newtonsoft.Json.Linq.JObject.Parse(json);
                    p.Protocol      = "vmess";
                    p.ServerAddress = obj["add"]?.ToString() ?? "";
                    p.ServerPort    = obj["port"] != null ? int.Parse(obj["port"].ToString()) : 443;
                    p.Uuid          = obj["id"]?.ToString() ?? "";
                    p.Encryption    = obj["scy"]?.ToString() ?? "auto";
                    p.Transport     = obj["net"]?.ToString() ?? "tcp";
                    p.WsPath        = obj["path"]?.ToString() ?? "/";
                    p.WsHost        = obj["host"]?.ToString() ?? "";
                    p.Tls           = obj["tls"]?.ToString() == "tls";
                    p.Sni           = obj["sni"]?.ToString() ?? p.ServerAddress;
                }
                else if (link.StartsWith("vless://") || link.StartsWith("trojan://"))
                {
                    var uri = new Uri(link);
                    p.Protocol      = link.StartsWith("vless://") ? "vless" : "trojan";
                    p.Uuid          = uri.UserInfo ?? "";
                    p.ServerAddress = uri.Host ?? "";
                    p.ServerPort    = uri.Port > 0 ? uri.Port : 443;
                    var query = uri.Query.TrimStart('?');
                    var parms = new Dictionary<string, string>();
                    foreach (var part in query.Split('&'))
                    {
                        var kv = part.Split('=');
                        if (kv.Length == 2)
                            parms[kv[0]] = Uri.UnescapeDataString(kv[1]);
                    }
                    p.Transport     = parms.ContainsKey("type")     ? parms["type"]     : "tcp";
                    p.Tls           = parms.ContainsKey("security") && (parms["security"] == "tls" || parms["security"] == "reality");
                    p.Sni           = parms.ContainsKey("sni")      ? parms["sni"]      : p.ServerAddress;
                    p.WsPath        = parms.ContainsKey("path")     ? parms["path"]     : "/";
                    p.WsHost        = parms.ContainsKey("host")     ? parms["host"]     : "";
                    p.AllowInsecure = parms.ContainsKey("allowInsecure") && (parms["allowInsecure"] == "1" || parms["allowInsecure"] == "true");
                }
            }
            catch { }
        }
    }
}
