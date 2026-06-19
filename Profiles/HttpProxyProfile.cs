using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de HttpProxyProfile.kt</summary>
    public class HttpProxyProfile
    {
        [JsonProperty("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")] public string ProfileName { get; set; } = "";

        // SSH
        [JsonProperty("sshHost")] public string SshHost { get; set; } = "";
        [JsonProperty("sshPort")] public int SshPort { get; set; } = 22;
        [JsonProperty("sshUser")] public string SshUser { get; set; } = "";
        [JsonProperty("sshPass")] public string SshPass { get; set; } = "";

        // Proxy
        [JsonProperty("proxyHost")] public string ProxyHost { get; set; } = "";
        [JsonProperty("proxyPort")] public int ProxyPort { get; set; } = 8080;
        [JsonProperty("customPayload")] public string CustomPayload { get; set; } =
            "GET / HTTP/1.1[crlf]Host: [host][crlf]Connection: Keep-Alive[crlf]Upgrade: websocket[crlf][crlf]";

        [JsonProperty("isSelected")] public bool IsSelected { get; set; } = false;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static HttpProxyProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<HttpProxyProfile>(json) ?? new HttpProxyProfile();

        public static List<HttpProxyProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<HttpProxyProfile>>(json) ?? new List<HttpProxyProfile>();

        public static string ListToJson(List<HttpProxyProfile> list) => JsonConvert.SerializeObject(list);
    }
}
