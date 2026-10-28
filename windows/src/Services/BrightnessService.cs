using System;
using System.Runtime.InteropServices;

namespace Navbar.Services
{
    /// <summary>
    /// Luminosité de l'écran principal : WMI pour les écrans de portable, DDC/CI pour les écrans externes.
    /// Les appels peuvent être lents (bus DDC) : à exécuter hors du thread d'interface.
    /// </summary>
    public sealed class BrightnessService
    {
        /// <summary>Luminosité actuelle (0-100), ou null si l'écran ne la permet pas.</summary>
        public int? Get()
        {
            return GetWmi() ?? GetDdc();
        }

        public bool Set(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            return SetWmi(percent) || SetDdc(percent);
        }

        // ───────────── WMI (écrans internes) ─────────────

        private static int? GetWmi()
        {
            try
            {
                using var s = new System.Management.ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (System.Management.ManagementObject o in s.Get())
                    return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return null;
        }

        private static bool SetWmi(int percent)
        {
            try
            {
                using var s = new System.Management.ManagementObjectSearcher(
                    @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (System.Management.ManagementObject o in s.Get())
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                    return true;
                }
            }
            catch { }
            return false;
        }

        // ───────────── DDC/CI (écrans externes) ─────────────

        private static int? GetDdc()
        {
            return WithPrimaryMonitor(h =>
            {
                if (!GetMonitorBrightness(h, out uint min, out uint cur, out uint max) || max <= min) return (int?)null;
                return (int)Math.Round((cur - min) * 100.0 / (max - min));
            });
        }

        private static bool SetDdc(int percent)
        {
            return WithPrimaryMonitor(h =>
            {
                if (!GetMonitorBrightness(h, out uint min, out _, out uint max) || max <= min) return (bool?)false;
                return SetMonitorBrightness(h, (uint)Math.Round(min + (max - min) * percent / 100.0));
            }) ?? false;
        }

        private static T? WithPrimaryMonitor<T>(Func<IntPtr, T?> action) where T : struct
        {
            try
            {
                IntPtr monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1 /*MONITOR_DEFAULTTOPRIMARY*/);
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0) return null;

                var monitors = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)) return null;
                try { return action(monitors[0].hPhysicalMonitor); }
                finally { DestroyPhysicalMonitors(count, monitors); }
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
        private struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll")] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll")] private static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(IntPtr h, uint value);
        [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
    }
}
