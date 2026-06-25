using Newtonsoft.Json;

namespace KighmuVpnWindows.Models
{
    /// <summary>Équivalent exact de HysteriaConfig.kt</summary>
    public class HysteriaConfig
    {
        [JsonProperty("serverAddress")] public string ServerAddress { get; set; } = "";
        [JsonProperty("serverPort")]    public int    ServerPort    { get; set; } = 20000;
        [JsonProperty("sni")]           public string Sni           { get; set; } = "";
        [JsonProperty("obfs")]          public string Obfs          { get; set; } = "";
        [JsonProperty("authPassword")] public string AuthPassword { get; set; } = "";
        [JsonProperty("uploadMbps")] public int UploadMbps { get; set; } = 10;
        [JsonProperty("downloadMbps")] public int DownloadMbps { get; set; } = 50;
        [JsonProperty("obfsPassword")] public string ObfsPassword { get; set; } = "";
        [JsonProperty("portHopping")] public string PortHopping { get; set; } = "20000-50000";
        [JsonProperty("protocol")] public string Protocol { get; set; } = "";
    }
}

