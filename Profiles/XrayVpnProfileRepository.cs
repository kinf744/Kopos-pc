using KighmuVpnWindows.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Equivalent exact de XrayVpnProfileRepository.kt</summary>
    public class XrayVpnProfileRepository
    {
        private readonly string _filePath;
        private const string FileName = "xrayvpn_profiles.json";

        public XrayVpnProfileRepository()
        {
            string dir = LocalStorage.GetAppDataDir();
            _filePath = Path.Combine(dir, FileName);
        }

        public List<XrayVpnProfile> GetAll()
        {
            if (!File.Exists(_filePath)) return new List<XrayVpnProfile>();
            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonConvert.DeserializeObject<List<XrayVpnProfile>>(json)
                       ?? new List<XrayVpnProfile>();
            }
            catch { return new List<XrayVpnProfile>(); }
        }

        public void Save(List<XrayVpnProfile> profiles)
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(profiles, Formatting.Indented));
        }

        public void Add(XrayVpnProfile profile)
        {
            var list = GetAll();
            list.Add(profile);
            Save(list);
        }

        public void Update(XrayVpnProfile profile)
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
            var cloned = new XrayVpnProfile
            {
                Id          = Guid.NewGuid().ToString(),
                ProfileName = original.ProfileName + " (copy)",
                ActiveMode  = original.ActiveMode,
                XrayLink    = original.XrayLink,
                XrayLinkJson= original.XrayLinkJson,
                XrayJson    = original.XrayJson,
                Protocol    = original.Protocol,
                ServerAddress = original.ServerAddress,
                ServerPort  = original.ServerPort,
                Uuid        = original.Uuid,
                Encryption  = original.Encryption,
                Transport   = original.Transport,
                WsPath      = original.WsPath,
                WsHost      = original.WsHost,
                Tls         = original.Tls,
                Sni         = original.Sni,
                Fingerprint = original.Fingerprint,
                AllowInsecure = original.AllowInsecure,
                PublicKey   = original.PublicKey,
                ShortId     = original.ShortId,
                GrpcServiceName = original.GrpcServiceName,
                KcpSeed     = original.KcpSeed,
                KcpHeader   = original.KcpHeader,
                Flow        = original.Flow,
                IsSelected  = false
            };
            list.Add(cloned);
            Save(list);
        }

        public List<XrayVpnProfile> GetSelected() =>
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
    }
}
