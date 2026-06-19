using KighmuVpnWindows.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de V2rayDnsProfileRepository.kt</summary>
    public class V2rayDnsProfileRepository
    {
        private readonly LocalStorage _prefs = new LocalStorage("v2raydns_profiles");
        private const string KEY = "profiles_json";

        public List<V2rayDnsProfile> GetAll() => V2rayDnsProfile.ListFromJson(_prefs.GetString(KEY, "[]"));

        public void Save(List<V2rayDnsProfile> profiles) =>
            _prefs.SetString(KEY, V2rayDnsProfile.ListToJson(profiles));

        public void Add(V2rayDnsProfile profile)
        {
            var list = GetAll();
            list.Add(profile);
            Save(list);
        }

        public void Update(V2rayDnsProfile profile)
        {
            var list = GetAll();
            int idx = list.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0) { list[idx] = profile; Save(list); }
        }

        public void Delete(string id) =>
            Save(GetAll().Where(p => p.Id != id).ToList());

        public void Clone(string id)
        {
            var list = GetAll();
            var original = list.FirstOrDefault(p => p.Id == id);
            if (original == null) return;

            var cloned = V2rayDnsProfile.FromJson(original.ToJson());
            cloned.Id = Guid.NewGuid().ToString();
            cloned.ProfileName = $"{original.ProfileName} (copy)";
            list.Add(cloned);
            Save(list);
        }

        public List<V2rayDnsProfile> GetSelected() => GetAll().Where(p => p.IsSelected).ToList();

        public void UpdateSelection(string id, bool selected)
        {
            var list = GetAll();
            var profile = list.FirstOrDefault(p => p.Id == id);
            if (profile != null) { profile.IsSelected = selected; Save(list); }
        }
    }
}
