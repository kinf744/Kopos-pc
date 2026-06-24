using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de HysteriaProfile.kt</summary>
    public class HysteriaProfile
    {
        [JsonProperty("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")] public string ProfileName { get; set; } = "";
        [JsonProperty("serverAddress")] public string ServerAddress { get; set; } = "";
        [JsonProperty("serverPort")]    public int    ServerPort    { get; set; } = 36712;
        [JsonProperty("sni")]           public string Sni           { get; set; } = "";
        [JsonProperty("obfs")]          public string Obfs          { get; set; } = "";

        [JsonProperty("authPassword")] public string AuthPassword { get; set; } = "";
        [JsonProperty("uploadMbps")] public int UploadMbps { get; set; } = 100;
        [JsonProperty("downloadMbps")] public int DownloadMbps { get; set; } = 100;
        [JsonProperty("obfsPassword")] public string ObfsPassword { get; set; } = "";
        [JsonProperty("portHopping")] public string PortHopping { get; set; } = "20000-50000";
        [JsonProperty("protocol")] public string Protocol { get; set; } = "udp";

        [JsonProperty("isSelected")] public bool IsSelected { get; set; } = false;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static HysteriaProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<HysteriaProfile>(json) ?? new HysteriaProfile();

        public static List<HysteriaProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<HysteriaProfile>>(json) ?? new List<HysteriaProfile>();

        public static string ListToJson(List<HysteriaProfile> list) => JsonConvert.SerializeObject(list);
    }
}
