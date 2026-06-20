using KighmuVpnWindows.Config;
using System.Collections.Generic;
using System.Linq;

namespace KighmuVpnWindows.Profiles
{
    public class SshSslProfileRepository
    {
        private const string PREFS_NAME = "sshssl_profiles";
        private const string KEY        = "profiles";
        private readonly LocalStorage _storage = new LocalStorage(PREFS_NAME);

        public List<SshSslProfile> GetAll()
        {
            var json = _storage.GetString(KEY, "[]");
            return SshSslProfile.ListFromJson(json);
        }

        public void SaveAll(List<SshSslProfile> profiles)
        {
            _storage.SetString(KEY, SshSslProfile.ListToJson(profiles));
        }

        public void Add(SshSslProfile profile)
        {
            var list = GetAll();
            list.Add(profile);
            SaveAll(list);
        }

        public void Update(SshSslProfile profile)
        {
            var list = GetAll();
            var idx  = list.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0) list[idx] = profile;
            else list.Add(profile);
            SaveAll(list);
        }

        public void Delete(string id)
        {
            var list = GetAll();
            list.RemoveAll(p => p.Id == id);
            SaveAll(list);
        }

        /// <summary>Retourne le profil actif (un seul, pas de multi)</summary>
        public SshSslProfile? GetActive()
        {
            return GetAll().FirstOrDefault(p => p.IsSelected)
                ?? GetAll().FirstOrDefault();
        }

        public void SetSelected(string id)
        {
            var list = GetAll();
            foreach (var p in list)
                p.IsSelected = (p.Id == id);
            SaveAll(list);
        }
        public void DeleteAll() => Save(new System.Collections.Generic.List<SshSslProfile>());
    }
}
