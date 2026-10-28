using System;
using System.Linq;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Navbar.Services
{
    /// <summary>Choix de l'écran de la pastille et calcul de l'échelle selon sa résolution.</summary>
    public static class ScreenService
    {
        private const double ReferenceWidth = 1920;

        /// <summary>Écran demandé par son nom Windows (« \\.\DISPLAY2 ») ; l'écran principal si absent ou débranché.</summary>
        public static Forms.Screen Resolve(string deviceName) =>
            Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];

        /// <summary>Facteur DPI de l'écran (1,0 = 96 dpi, 1,5 = 150 %…).</summary>
        public static double Dpi(Forms.Screen screen)
        {
            try
            {
                var center = new POINT
                {
                    X = screen.Bounds.Left + screen.Bounds.Width / 2,
                    Y = screen.Bounds.Top + screen.Bounds.Height / 2,
                };
                IntPtr monitor = MonitorFromPoint(center, 2 /*MONITOR_DEFAULTTONEAREST*/);
                GetDpiForMonitor(monitor, 0 /*MDT_EFFECTIVE_DPI*/, out uint dpiX, out _);
                return dpiX > 0 ? dpiX / 96.0 : 1.0;
            }
            catch { return 1.0; }
        }

        /// <summary>Échelle de l'encoche : largeur de l'écran en unités indépendantes de la densité, rapportée à 1920.</summary>
        public static double GetScale(Forms.Screen screen) =>
            Math.Clamp(screen.Bounds.Width / Dpi(screen) / ReferenceWidth, 0.8, 1.4);

        /// <summary>« 1 · 1920×1080 » avec une étoile pour l'écran principal.</summary>
        public static string Describe(Forms.Screen screen, int index) =>
            $"{index + 1}{(screen.Primary ? "★" : "")} · {screen.Bounds.Width}×{screen.Bounds.Height}";

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    }
}
