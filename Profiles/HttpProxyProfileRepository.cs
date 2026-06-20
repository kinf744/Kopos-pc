using KighmuVpnWindows.Config;
using KighmuVpnWindows.Utils;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    public class HttpProxyProfileRepository
    {
        private readonly string _filePath;
        private const string FileName = "httpproxy_profiles.json";

        public HttpProxyProfileRepository()
        {
            string dir = LocalStorage.GetAppDataDir();
            _filePath = Path.Combine(dir, FileName);
        }

        public List<HttpProxyProfile> GetAll()
        {
            if (!File.Exists(_filePath)) return new List<HttpProxyProfile>();
            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonConvert.DeserializeObject<List<HttpProxyProfile>>(json)
                       ?? new List<HttpProxyProfile>();
            }
            catch { return new List<HttpProxyProfile>(); }
        }

        public void Save(List<HttpProxyProfile> profiles)
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(profiles, Formatting.Indented));
        }

        public void Add(HttpProxyProfile profile)
        {
            var list = GetAll();
            list.Add(profile);
            Save(list);
        }

        public void Update(HttpProxyProfile profile)
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
            var cloned = new HttpProxyProfile
            {
                Id            = System.Guid.NewGuid().ToString(),
                ProfileName   = original.ProfileName + " (copy)",
                SshHost       = original.SshHost,
                SshPort       = original.SshPort,
                SshUser       = original.SshUser,
                SshPass       = original.SshPass,
                ProxyHost     = original.ProxyHost,
                ProxyPort     = original.ProxyPort,
                CustomPayload = original.CustomPayload,
                IsSelected    = false
            };
            list.Add(cloned);
            Save(list);
        }

        public List<HttpProxyProfile> GetSelected() =>
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
