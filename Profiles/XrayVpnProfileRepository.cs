using KighmuVpnWindows.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de XrayVpnProfileRepository.kt</summary>
    public class XrayVpnProfileRepository
    {
        private readonly LocalStorage _prefs = new LocalStorage("xrayvpn_profiles");
        private const string KEY = "profiles_json";

        public List<XrayVpnProfile> GetAll() => XrayVpnProfile.ListFromJson(_prefs.GetString(KEY, "[]"));

        public void Save(List<XrayVpnProfile> profiles) =>
            _prefs.SetString(KEY, XrayVpnProfile.ListToJson(profiles));

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

        public void Delete(string id) =>
            Save(GetAll().Where(p => p.Id != id).ToList());

        public void Clone(string id)
        {
            var list = GetAll();
            var original = list.FirstOrDefault(p => p.Id == id);
            if (original == null) return;

            var cloned = XrayVpnProfile.FromJson(original.ToJson());
            cloned.Id = Guid.NewGuid().ToString();
            cloned.ProfileName = $"{original.ProfileName} (copy)";
            list.Add(cloned);
            Save(list);
        }

        public List<XrayVpnProfile> GetSelected() => GetAll().Where(p => p.IsSelected).ToList();

        public void UpdateSelection(string id, bool selected)
        {
            var list = GetAll();
            var profile = list.FirstOrDefault(p => p.Id == id);
            if (profile != null) { profile.IsSelected = selected; Save(list); }
        }
    }
}
