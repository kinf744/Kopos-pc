using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace KighmuVpnWindows.Profiles
{
    public class SshSslProfile
    {
        [JsonProperty("id")]           public string Id          { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("profileName")]  public string ProfileName { get; set; } = "";
        [JsonProperty("sshHost")]      public string SshHost     { get; set; } = "";
        [JsonProperty("sshPort")]      public int    SshPort     { get; set; } = 443;
        [JsonProperty("sshUser")]      public string SshUser     { get; set; } = "";
        [JsonProperty("sshPass")]      public string SshPass     { get; set; } = "";
        [JsonProperty("sni")]          public string Sni         { get; set; } = "";
        [JsonProperty("tlsVersion")]   public string TlsVersion  { get; set; } = "TLS";
        [JsonProperty("allowInsecure")] public bool  AllowInsecure { get; set; } = true;
        [JsonProperty("isSelected")]   public bool   IsSelected   { get; set; } = false;

        public string ToJson() => JsonConvert.SerializeObject(this);
        public static SshSslProfile FromJson(string json) =>
            JsonConvert.DeserializeObject<SshSslProfile>(json) ?? new SshSslProfile();
        public static List<SshSslProfile> ListFromJson(string json) =>
            JsonConvert.DeserializeObject<List<SshSslProfile>>(json) ?? new List<SshSslProfile>();
        public static string ListToJson(List<SshSslProfile> list) =>
            JsonConvert.SerializeObject(list);
    }
}
