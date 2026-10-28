using System;
using Microsoft.Win32;

namespace Navbar.Services
{
    /// <summary>Lancement automatique à l'ouverture de session (HKCU\...\Run, sans droits admin).</summary>
    public static class AutoStart
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Navbar";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key == null) return;
            if (enabled) key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
