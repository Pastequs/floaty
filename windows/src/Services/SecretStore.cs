using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Navbar.Services
{
    /// <summary>
    /// Stockage des clés d'API et jetons : chiffrés avec DPAPI (liés à ton compte Windows) dans %AppData%\Navbar\secrets.json.
    /// Une clé n'est jamais écrite en clair, ni dans les réglages, ni dans les journaux.
    /// </summary>
    public static class SecretStore
    {
        private static Dictionary<string, string>? _cache;

        private static Dictionary<string, string> Load() =>
            _cache ??= Storage.Load("secrets", new Dictionary<string, string>());

        public static string Get(string name)
        {
            if (!Load().TryGetValue(name, out var protectedBase64) || protectedBase64.Length == 0) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }   // fichier copié d'un autre compte ou corrompu : comme s'il n'y avait pas de clé
        }

        public static bool Has(string name) => Get(name).Length > 0;

        public static void Set(string name, string value)
        {
            var map = Load();
            if (string.IsNullOrEmpty(value)) map.Remove(name);
            else map[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            Storage.Save("secrets", map);
        }
    }
}
