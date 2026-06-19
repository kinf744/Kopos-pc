using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de XrayVpnProfile.kt</summary>
    public class XrayVpnProfile
    {
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

        [JsonProperty("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")] public string ProfileName { get; set; } = "";

        // Mode actif : "link" | "json"
        [JsonProperty("activeMode")] public string ActiveMode { get; set; } = "json";

        // Mode lien
        [JsonProperty("xrayLink")] public string XrayLink { get; set; } = "";
        [JsonProperty("xrayLinkJson")] public string XrayLinkJson { get; set; } = "";

        // Mode JSON direct
        [JsonProperty("xrayJson")] public string XrayJson { get; set; } = "";

        // Champs parsés depuis le lien
        [JsonProperty("protocol")] public string Protocol { get; set; } = "vmess";
        [JsonProperty("serverAddress")] public string ServerAddress { get; set; } = "";
        [JsonProperty("serverPort")] public int ServerPort { get; set; } = 443;
        [JsonProperty("uuid")] public string Uuid { get; set; } = "";
        [JsonProperty("encryption")] public string Encryption { get; set; } = "auto";

        // Transport : ws | tcp | xhttp | grpc | h2 | kcp | httpupgrade | splithttp
        [JsonProperty("transport")] public string Transport { get; set; } = "ws";
        [JsonProperty("wsPath")] public string WsPath { get; set; } = "/";
        [JsonProperty("wsHost")] public string WsHost { get; set; } = "";

        // TLS / Reality
        [JsonProperty("tls")] public bool Tls { get; set; } = true;
        [JsonProperty("sni")] public string Sni { get; set; } = "";
        [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = "chrome";
        [JsonProperty("allowInsecure")] public bool AllowInsecure { get; set; } = false;

        // Reality specifique
        [JsonProperty("publicKey")] public string PublicKey { get; set; } = "";
        [JsonProperty("shortId")] public string ShortId { get; set; } = "";

        // gRPC
        [JsonProperty("grpcServiceName")] public string GrpcServiceName { get; set; } = "";

        // kCP
        [JsonProperty("kcpSeed")] public string KcpSeed { get; set; } = "";
        [JsonProperty("kcpHeader")] public string KcpHeader { get; set; } = "none";

        // VLESS flow (xtls-rprx-vision)
        [JsonProperty("flow")] public string Flow { get; set; } = "";

        [JsonProperty("isSelected")] public bool IsSelected { get; set; } = false;

        public string GetActiveJson() => ActiveMode switch
        {
            "link" => string.IsNullOrWhiteSpace(XrayLinkJson) ? XrayJson : XrayLinkJson,
            _ => string.IsNullOrWhiteSpace(XrayJson) ? DEFAULT_JSON : XrayJson
        };

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static XrayVpnProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<XrayVpnProfile>(json) ?? new XrayVpnProfile();

        public static List<XrayVpnProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<XrayVpnProfile>>(json) ?? new List<XrayVpnProfile>();

        public static string ListToJson(List<XrayVpnProfile> list) => JsonConvert.SerializeObject(list);
    }
}
