using Newtonsoft.Json;

namespace KighmuVpnWindows.Models
{
    /// <summary>Equivalent exact de SshSslConfig (TunnelConfig.kt)</summary>
    public class SshSslConfig
    {
        [JsonProperty("sshHost")]      public string SshHost      { get; set; } = "";
        [JsonProperty("sshPort")]      public int    SshPort      { get; set; } = 443;
        [JsonProperty("sshUser")]      public string SshUser      { get; set; } = "";
        [JsonProperty("sshPass")]      public string SshPass      { get; set; } = "";
        [JsonProperty("sni")]          public string Sni          { get; set; } = "";
        [JsonProperty("tlsVersion")]   public string TlsVersion   { get; set; } = "TLS";
        [JsonProperty("allowInsecure")]public bool   AllowInsecure{ get; set; } = true;
    }
}
