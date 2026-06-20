using KighmuVpnWindows.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de ProfileRepository.kt (gère les profils SlowDNS)</summary>
    public class SlowDnsProfileRepository
    {
        private readonly LocalStorage _prefs = new LocalStorage("slowdns_profiles");
        private const string KEY = "profiles_json";

        public List<SlowDnsProfile> GetAll()
        {
            try
            {
                return SlowDnsProfile.ListFromJson(_prefs.GetString(KEY, "[]"));
            }
            catch
            {
                return new List<SlowDnsProfile>();
            }
        }

        public void Save(List<SlowDnsProfile> profiles) =>
            _prefs.SetString(KEY, SlowDnsProfile.ListToJson(profiles));

        public void Add(SlowDnsProfile profile)
        {
            var list = GetAll();
            list.Add(profile);
            Save(list);
        }

        public void Update(SlowDnsProfile profile)
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

            var cloned = SlowDnsProfile.FromJson(original.ToJson());
            cloned.Id = Guid.NewGuid().ToString();
            cloned.ProfileName = $"{original.ProfileName} (copy)";
            list.Add(cloned);
            Save(list);
        }

        public List<SlowDnsProfile> GetSelected() => GetAll().Where(p => p.IsSelected).ToList();

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
