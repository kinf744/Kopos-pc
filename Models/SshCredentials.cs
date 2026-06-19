using Newtonsoft.Json;

namespace KighmuVpnWindows.Models
{
    public class SshCredentials
    {
        [JsonProperty("host")] public string Host { get; set; } = "";
        [JsonProperty("port")] public int Port { get; set; } = 22;
        [JsonProperty("username")] public string Username { get; set; } = "";
        [JsonProperty("password")] public string Password { get; set; } = "";
        [JsonProperty("privateKey")] public string PrivateKey { get; set; } = "";
        [JsonProperty("usePrivateKey")] public bool UsePrivateKey { get; set; } = false;
    }
}
