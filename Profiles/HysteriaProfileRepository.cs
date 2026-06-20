using KighmuVpnWindows.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    /// <summary>Équivalent exact de HysteriaProfileRepository.kt</summary>
    public class HysteriaProfileRepository
    {
        private readonly LocalStorage _prefs = new LocalStorage("hysteria_profiles");
        private const string KEY = "profiles_json";

        public List<HysteriaProfile> GetAll() => HysteriaProfile.ListFromJson(_prefs.GetString(KEY, "[]"));

        public void Save(List<HysteriaProfile> profiles) =>
            _prefs.SetString(KEY, HysteriaProfile.ListToJson(profiles));

        public void Add(HysteriaProfile profile)
        {
            var list = GetAll();
            list.Add(profile);
            Save(list);
        }

        public void Update(HysteriaProfile profile)
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

            var cloned = HysteriaProfile.FromJson(original.ToJson());
            cloned.Id = Guid.NewGuid().ToString();
            cloned.ProfileName = $"{original.ProfileName} (copy)";
            list.Add(cloned);
            Save(list);
        }

        public List<HysteriaProfile> GetSelected() => GetAll().Where(p => p.IsSelected).ToList();

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
