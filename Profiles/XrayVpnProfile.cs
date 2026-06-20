using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Equivalent exact de XrayVpnProfile.kt</summary>
    public class XrayVpnProfile
    {
        [JsonProperty("id")]          public string Id          { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")] public string ProfileName { get; set; } = "";

        // Mode actif : "link" | "json"
        [JsonProperty("activeMode")]  public string ActiveMode  { get; set; } = "json";

        // Mode lien
        [JsonProperty("xrayLink")]     public string XrayLink     { get; set; } = "";
        [JsonProperty("xrayLinkJson")] public string XrayLinkJson { get; set; } = "";

        // Mode JSON direct
        [JsonProperty("xrayJson")]     public string XrayJson     { get; set; } = "";

        // Champs parses depuis le lien
        [JsonProperty("protocol")]      public string Protocol      { get; set; } = "vmess";
        [JsonProperty("serverAddress")] public string ServerAddress { get; set; } = "";
        [JsonProperty("serverPort")]    public int    ServerPort    { get; set; } = 443;
        [JsonProperty("uuid")]          public string Uuid          { get; set; } = "";
        [JsonProperty("encryption")]    public string Encryption    { get; set; } = "auto";

        // Transport : ws | tcp | xhttp | grpc | h2 | kcp | httpupgrade | splithttp
        [JsonProperty("transport")]  public string Transport  { get; set; } = "ws";
        [JsonProperty("wsPath")]     public string WsPath     { get; set; } = "/";
        [JsonProperty("wsHost")]     public string WsHost     { get; set; } = "";

        // TLS / Reality
        [JsonProperty("tls")]           public bool   Tls           { get; set; } = true;
        [JsonProperty("sni")]           public string Sni           { get; set; } = "";
        [JsonProperty("fingerprint")]   public string Fingerprint   { get; set; } = "chrome";
        [JsonProperty("allowInsecure")] public bool   AllowInsecure { get; set; } = false;

        // Reality specifique
        [JsonProperty("publicKey")] public string PublicKey { get; set; } = "";
        [JsonProperty("shortId")]   public string ShortId   { get; set; } = "";

        // gRPC
        [JsonProperty("grpcServiceName")] public string GrpcServiceName { get; set; } = "";

        // kCP
        [JsonProperty("kcpSeed")]   public string KcpSeed   { get; set; } = "";
        [JsonProperty("kcpHeader")] public string KcpHeader { get; set; } = "none";

        // VLESS flow (xtls-rprx-vision)
        [JsonProperty("flow")] public string Flow { get; set; } = "";

        // Etat
        [JsonProperty("isSelected")] public bool IsSelected { get; set; } = false;

        public const string DEFAULT_JSON = @"{
  ""log"": { ""loglevel"": ""warning"" },
  ""inbounds"": [{ ""port"": 10808, ""protocol"": ""socks"", ""settings"": { ""udp"": true } }],
  ""outbounds"": [{
    ""protocol"": ""vmess"",
    ""settings"": { ""vnext"": [{ ""address"": ""example.com"", ""port"": 443,
      ""users"": [{ ""id"": ""your-uuid-here"", ""alterId"": 0 }] }] },
    ""streamSettings"": { ""network"": ""ws"", ""security"": ""tls"",
      ""wsSettings"": { ""path"": ""/"" },
      ""tlsSettings"": { ""serverName"": ""example.com"" } }
  }, { ""protocol"": ""freedom"", ""tag"": ""direct"" }],
  ""routing"": { ""rules"": [] }
}";

        /// <summary>Retourne le JSON actif selon le mode (link ou json) - equivalent de getActiveJson()</summary>
        public string GetActiveJson()
        {
            if (ActiveMode == "link")
                return !string.IsNullOrWhiteSpace(XrayLinkJson) ? XrayLinkJson : XrayJson;
            return !string.IsNullOrWhiteSpace(XrayJson) ? XrayJson : DEFAULT_JSON;
        }

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static XrayVpnProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<XrayVpnProfile>(json) ?? new XrayVpnProfile();

        public static List<XrayVpnProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<XrayVpnProfile>>(json) ?? new List<XrayVpnProfile>();

        public static string ListToJson(List<XrayVpnProfile> list) =>
            JsonConvert.SerializeObject(list);

        public static void ParseLinkIntoProfile(string link, XrayVpnProfile p)
        {
            try
            {
                if (link.StartsWith("vmess://"))
                {
                    var b64  = link.Substring("vmess://".Length);
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    var obj  = Newtonsoft.Json.Linq.JObject.Parse(json);
                    string transport = obj["net"]?.ToString() ?? "tcp";
                    string tls       = obj["tls"]?.ToString() ?? "";
                    string sni       = obj["sni"]?.ToString() ?? obj["add"]?.ToString() ?? "";
                    string path      = obj["path"]?.ToString() ?? "/";
                    string wsHost    = obj["host"]?.ToString() ?? obj["add"]?.ToString() ?? "";
                    string security  = tls == "tls" ? "tls" : "none";
                    p.Protocol      = "vmess";
                    p.ServerAddress = obj["add"]?.ToString()  ?? "";
                    p.ServerPort    = obj["port"] != null ? int.Parse(obj["port"].ToString()) : 443;
                    p.Uuid          = obj["id"]?.ToString()   ?? "";
                    p.Encryption    = obj["scy"]?.ToString()  ?? "auto";
                    p.Transport     = transport;
                    p.WsPath        = path;
                    p.WsHost        = wsHost;
                    p.Tls           = tls == "tls";
                    p.Sni           = sni;
                    p.XrayLinkJson  = BuildVmessJson(obj, transport, security, sni, path, wsHost);
                }
                else if (link.StartsWith("vless://") || link.StartsWith("trojan://"))
                {
                    var uri    = new Uri(link);
                    string proto = link.StartsWith("vless://") ? "vless" : "trojan";
                    var parms  = new Dictionary<string, string>();
                    foreach (var part in (uri.Query ?? "").TrimStart('?').Split('&'))
                    {
                        var kv = part.Split('=');
                        if (kv.Length == 2)
                            parms[kv[0]] = Uri.UnescapeDataString(kv[1]);
                    }
                    string transport = parms.ContainsKey("type")     ? parms["type"]     : "tcp";
                    string security  = parms.ContainsKey("security") ? parms["security"] : "none";
                    string sni       = parms.ContainsKey("sni")      ? parms["sni"]      : uri.Host ?? "";
                    string path      = parms.ContainsKey("path")     ? parms["path"]     : "/";
                    string wsHost    = parms.ContainsKey("host")     ? parms["host"]     : sni;
                    string fp        = parms.ContainsKey("fp")       ? parms["fp"]       : "chrome";
                    string pbk       = parms.ContainsKey("pbk")      ? parms["pbk"]      : "";
                    string sid       = parms.ContainsKey("sid")       ? parms["sid"]      : "";
                    string flow      = parms.ContainsKey("flow")     ? parms["flow"]     : "";
                    p.Protocol      = proto;
                    p.Uuid          = uri.UserInfo ?? "";
                    p.ServerAddress = uri.Host ?? "";
                    p.ServerPort    = uri.Port > 0 ? uri.Port : 443;
                    p.Transport     = transport;
                    p.Tls           = security == "tls" || security == "reality";
                    p.Sni           = sni;
                    p.WsPath        = path;
                    p.WsHost        = wsHost;
                    p.Fingerprint   = fp;
                    p.PublicKey     = pbk;
                    p.ShortId       = sid;
                    p.Flow          = flow;
                    p.AllowInsecure = parms.ContainsKey("allowInsecure") && parms["allowInsecure"] == "1";
                    p.XrayLinkJson  = BuildVlessOrTrojanJson(proto, p.Uuid, p.ServerAddress, p.ServerPort,
                                        transport, security, sni, path, wsHost, fp, pbk, sid, flow);
                }
            }
            catch { }
        }

        private static string StreamSettings(string transport, string security, string sni,
            string path, string host, string fp = "chrome", string pbk = "", string sid = "")
        {
            string tlsPart;
            if (security == "tls")
                tlsPart = $",\"tlsSettings\":{{\"serverName\":\"{sni}\",\"fingerprint\":\"{fp}\"}}";
            else if (security == "reality")
                tlsPart = $",\"realitySettings\":{{\"serverName\":\"{sni}\",\"fingerprint\":\"{fp}\",\"publicKey\":\"{pbk}\",\"shortId\":\"{sid}\"}}";
            else
                tlsPart = "";

            string net = transport == "mkcp" ? "kcp" : transport == "raw" ? "tcp" : transport;
            string netPart;
            if (transport == "ws")
                netPart = $",\"wsSettings\":{{\"path\":\"{path}\",\"headers\":{{\"Host\":\"{host}\"}}}}";
            else if (transport == "grpc")
                netPart = $",\"grpcSettings\":{{\"serviceName\":\"{path}\"}}";
            else if (transport == "xhttp")
                netPart = $",\"xhttpSettings\":{{\"path\":\"{path}\",\"host\":\"{host}\",\"mode\":\"stream-up\"}}";
            else if (transport == "splithttp")
                netPart = $",\"splithttpSettings\":{{\"path\":\"{path}\",\"host\":\"{host}\"}}";
            else if (transport == "h2" || transport == "http")
                netPart = $",\"httpSettings\":{{\"path\":\"{path}\",\"host\":[\"\"{host}\"\"]}}";
            else if (transport == "httpupgrade")
                netPart = $",\"httpupgradeSettings\":{{\"path\":\"{path}\",\"host\":\"{host}\"}}";
            else if (transport == "kcp" || transport == "mkcp")
                netPart = $",\"kcpSettings\":{{\"mtu\":1350,\"tti\":20,\"uplinkCapacity\":5,\"downlinkCapacity\":20,\"congestion\":false,\"readBufferSize\":2,\"writeBufferSize\":2,\"header\":{{\"type\":\"none\"}}}}";
            else
                netPart = ",\"tcpSettings\":{{\"header\":{{\"type\":\"none\"}}}}";

            return $"{{\"network\":\"{net}\",\"security\":\"{security}\"{tlsPart}{netPart}}}";
        }

        private static string BuildVmessJson(Newtonsoft.Json.Linq.JObject obj,
            string transport, string security, string sni, string path, string wsHost)
        {
            string host    = obj["add"]?.ToString() ?? "";
            int    port    = obj["port"] != null ? int.Parse(obj["port"].ToString()) : 443;
            string uuid    = obj["id"]?.ToString()  ?? "";
            int    alterId = obj["aid"] != null ? int.Parse(obj["aid"].ToString()) : 0;
            string stream  = StreamSettings(transport, security, sni, path, wsHost);
            return string.Concat(
                "{\"log\":{\"loglevel\":\"warning\"},",
                "\"inbounds\":[{\"port\":10808,\"protocol\":\"socks\",\"settings\":{\"udp\":true}}],",
                "\"outbounds\":[",
                "{\"protocol\":\"vmess\",\"settings\":{\"vnext\":[{\"address\":\"", host, "\",\"port\":", port.ToString(),
                ",\"users\":[{\"id\":\"", uuid, "\",\"alterId\":", alterId.ToString(), ",\"security\":\"auto\"}]}]},",
                "\"streamSettings\":", stream, ",\"mux\":{\"enabled\":false}},",
                "{\"protocol\":\"freedom\",\"tag\":\"direct\"}],",
                "\"routing\":{\"rules\":[]}}");
        }

        private static string BuildVlessOrTrojanJson(string proto, string uuid, string host, int port,
            string transport, string security, string sni, string path, string wsHost,
            string fp, string pbk, string sid, string flow)
        {
            string stream   = StreamSettings(transport, security, sni, path, wsHost, fp, pbk, sid);
            string flowPart = !string.IsNullOrEmpty(flow) ? string.Concat(",\"flow\":\"", flow, "\"") : "";
            string outbound;
            if (proto == "trojan")
                outbound = string.Concat(
                    "{\"protocol\":\"trojan\",\"settings\":{\"servers\":[{\"address\":\"", host,
                    "\",\"port\":", port.ToString(), ",\"password\":\"", uuid, "\"}]},",
                    "\"streamSettings\":", stream, ",\"mux\":{\"enabled\":false}}");
            else
                outbound = string.Concat(
                    "{\"protocol\":\"vless\",\"settings\":{\"vnext\":[{\"address\":\"", host,
                    "\",\"port\":", port.ToString(), ",\"users\":[{\"id\":\"", uuid,
                    "\",\"encryption\":\"none\"", flowPart, "}]}]},",
                    "\"streamSettings\":", stream, ",\"mux\":{\"enabled\":false}}");
            return string.Concat(
                "{\"log\":{\"loglevel\":\"warning\"},",
                "\"inbounds\":[{\"port\":10808,\"protocol\":\"socks\",\"settings\":{\"udp\":true}}],",
                "\"outbounds\":[", outbound, ",{\"protocol\":\"freedom\",\"tag\":\"direct\"}],",
                "\"routing\":{\"rules\":[]}}");
        }
    }
}
