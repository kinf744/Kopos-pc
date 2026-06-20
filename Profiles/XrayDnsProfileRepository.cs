using KighmuVpnWindows.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Equivalent exact de XrayDnsProfileRepository.kt</summary>
    public class XrayDnsProfileRepository
    {
        private readonly string _filePath;
        private const string FileName = "xraydns_profiles.json";

        public XrayDnsProfileRepository()
        {
            string dir = LocalStorage.GetAppDataDir();
            _filePath = Path.Combine(dir, FileName);
        }

        public List<XrayDnsProfile> GetAll()
        {
            if (!File.Exists(_filePath)) return new List<XrayDnsProfile>();
            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonConvert.DeserializeObject<List<XrayDnsProfile>>(json)
                       ?? new List<XrayDnsProfile>();
            }
            catch { return new List<XrayDnsProfile>(); }
        }

        public void Save(List<XrayDnsProfile> profiles)
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(profiles, Formatting.Indented));
        }

        public void Add(XrayDnsProfile profile)
        {
            var list = GetAll();
            list.Add(profile);
            Save(list);
        }

        public void Update(XrayDnsProfile profile)
        {
            var list = GetAll();
            int idx = list.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0) { list[idx] = profile; Save(list); }
        }

        public void Delete(string id)
        {
            var list = GetAll().Where(p => p.Id != id).ToList();
            Save(list);
        }

        public void Clone(string id)
        {
            var list = GetAll();
            var original = list.FirstOrDefault(p => p.Id == id);
            if (original == null) return;
            var cloned = new XrayDnsProfile
            {
                Id            = Guid.NewGuid().ToString(),
                ProfileName   = original.ProfileName + " (copy)",
                XrayLink      = original.XrayLink,
                XrayJsonConfig= original.XrayJsonConfig,
                Protocol      = original.Protocol,
                ServerAddress = original.ServerAddress,
                ServerPort    = original.ServerPort,
                Uuid          = original.Uuid,
                Encryption    = original.Encryption,
                Transport     = original.Transport,
                WsPath        = original.WsPath,
                WsHost        = original.WsHost,
                Tls           = original.Tls,
                Sni           = original.Sni,
                AllowInsecure = original.AllowInsecure,
                DnsServer     = original.DnsServer,
                DnsPort       = original.DnsPort,
                Nameserver    = original.Nameserver,
                PublicKey     = original.PublicKey,
                TunnelCount   = original.TunnelCount,
                IsSelected    = false
            };
            list.Add(cloned);
            Save(list);
        }

        public List<XrayDnsProfile> GetSelected() =>
            GetAll().Where(p => p.IsSelected).ToList();

        public void UpdateSelection(string id, bool selected)
        {
            var list = GetAll();
            var profile = list.FirstOrDefault(p => p.Id == id);
            if (profile != null) { profile.IsSelected = selected; Save(list); }
        }

        public void SetSelected(string id, bool selected)
        {
            var list = GetAll();
            foreach (var p in list)
                if (p.Id == id) p.IsSelected = selected;
            Save(list);
        }
        public void DeleteAll() => Save(new System.Collections.Generic.List<XrayDnsProfile>());
    }
}
