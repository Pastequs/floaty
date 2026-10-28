using System;
using System.Runtime.InteropServices;

namespace Navbar.Services
{
    /// <summary>
    /// Masquage automatique de la barre des tâches Windows (l'option « Masquer automatiquement la barre des tâches » de Windows).
    /// La barre n'est ni arrêtée ni modifiée autrement : elle réapparaît quand la souris touche le bord de l'écran.
    /// </summary>
    public static class TaskbarService
    {
        private const uint ABM_GETSTATE = 0x4, ABM_SETSTATE = 0xA;
        private const int ABS_AUTOHIDE = 0x1;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }

        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);

        private static APPBARDATA Data() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null) };

        public static bool IsAutoHide()
        {
            var d = Data();
            return ((int)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) != 0;
        }

        /// <summary>Active ou désactive le masquage automatique en gardant l'autre drapeau (« toujours au premier plan »).</summary>
        public static void SetAutoHide(bool on)
        {
            var d = Data();
            int state = (int)SHAppBarMessage(ABM_GETSTATE, ref d);
            d.lParam = (IntPtr)(on ? state | ABS_AUTOHIDE : state & ~ABS_AUTOHIDE);
            SHAppBarMessage(ABM_SETSTATE, ref d);
        }
    }
}
