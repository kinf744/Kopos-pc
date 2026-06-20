using Newtonsoft.Json;
using System;

namespace KighmuVpnWindows.Config
{
    /// <summary>Modele de securite pour export/import de config - equivalent de ExportConfig.kt</summary>
    public class ConfigExport
    {
        [JsonProperty("fileName")]          public string FileName          { get; set; } = "";
        [JsonProperty("expiresAt")]         public long   ExpiresAt         { get; set; } = 0L;
        [JsonProperty("hardwareId")]        public string HardwareId        { get; set; } = "";
        [JsonProperty("lockDeviceId")]      public bool   LockDeviceId      { get; set; } = false;
        [JsonProperty("lockOperator")]      public bool   LockOperator      { get; set; } = false;
        [JsonProperty("operatorName")]      public string OperatorName      { get; set; } = "";
        [JsonProperty("accessCode")]        public string AccessCode        { get; set; } = "";
        [JsonProperty("userMessage")]       public string UserMessage       { get; set; } = "";
        [JsonProperty("burnAfterImport")]   public bool   BurnAfterImport   { get; set; } = false;
        [JsonProperty("burnToken")]         public string BurnToken         { get; set; } = "";
        [JsonProperty("lockAllConfig")]     public bool   LockAllConfig     { get; set; } = false;
        [JsonProperty("appId")]             public string AppId             { get; set; } = "";
        [JsonProperty("exportType")]        public string ExportType        { get; set; } = "normal";
        [JsonProperty("securitySignature")] public string SecuritySignature { get; set; } = "";
        [JsonProperty("exportedAt")]        public long   ExportedAt        { get; set; } = 0L;
        [JsonProperty("appVersion")]        public string AppVersion        { get; set; } = "";
    }
}
