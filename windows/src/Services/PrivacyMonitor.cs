using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Navbar.Services
{
    public sealed record PrivacyState(bool Microphone, bool Camera, bool Screen, IReadOnlyList<string> Apps)
    {
        public static readonly PrivacyState None = new(false, false, false, Array.Empty<string>());
        public bool Any => Microphone || Camera || Screen;
    }

    /// <summary>
    /// Détecte l'usage du micro, de la caméra et de la capture d'écran grâce au journal de consentement de Windows
    /// (le même que celui qui affiche le point orange de la barre des tâches). Lecture seule, aucun accès au micro.
    /// </summary>
    public sealed class PrivacyMonitor
    {
        private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        public PrivacyState Read()
        {
            var apps = new List<string>();
            bool mic = InUse("microphone", apps), cam = InUse("webcam", apps);
            bool screen = InUse("graphicsCaptureProgrammatic", apps) || InUse("graphicsCaptureWithoutBorder", apps);
            return new PrivacyState(mic, cam, screen, apps.Distinct().ToList());
        }

        /// <summary>Vrai si une application a ouvert la ressource sans l'avoir encore relâchée.</summary>
        private static bool InUse(string capability, List<string> apps)
        {
            bool any = false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{capability}");
                if (key == null) return false;

                foreach (var name in key.GetSubKeyNames())
                {
                    if (name == "NonPackaged")
                    {
                        using var nonPackaged = key.OpenSubKey(name);
                        foreach (var app in nonPackaged?.GetSubKeyNames() ?? Array.Empty<string>())
                            any |= Check(nonPackaged!, app, apps);
                    }
                    else any |= Check(key, name, apps);
                }
            }
            catch { /* clé absente ou protégée */ }
            return any;
        }

        private static bool Check(RegistryKey parent, string name, List<string> apps)
        {
            using var entry = parent.OpenSubKey(name);
            if (entry?.GetValue("LastUsedTimeStart") is not long start || start <= 0) return false;
            long stop = entry.GetValue("LastUsedTimeStop") is long s ? s : 0;
            if (stop >= start) return false;

            // Une entrée jamais refermée depuis plus d'une demi-journée est presque toujours un reste : on l'ignore
            if (DateTime.UtcNow - DateTime.FromFileTimeUtc(start) > TimeSpan.FromHours(12)) return false;

            string friendly = name.Replace('#', '\\');
            friendly = friendly.Contains('\\') ? System.IO.Path.GetFileNameWithoutExtension(friendly) : friendly.Split('_')[0];
            apps.Add(friendly);
            return true;
        }
    }
}
