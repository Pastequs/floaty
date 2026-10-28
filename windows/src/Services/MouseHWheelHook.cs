using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Navbar.Services
{
    /// <summary>
    /// Molette horizontale (souris à molette inclinable, pavé tactile) : Windows ne la transmet qu'à la fenêtre
    /// qui a le focus, or l'encoche ne le prend jamais. On l'intercepte donc globalement et on ne la consomme que
    /// si le curseur est sur l'encoche.
    /// </summary>
    public sealed class MouseHWheelHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEHWHEEL = 0x020E;

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        private readonly Func<bool> _shouldHandle;
        private readonly Action<int> _onWheel;
        private HookProc? _proc;
        private IntPtr _hook;

        /// <param name="shouldHandle">Vrai quand l'événement doit être consommé (curseur sur l'encoche).</param>
        /// <param name="onWheel">Reçoit le delta signé (120 = un cran vers la droite).</param>
        public MouseHWheelHook(Func<bool> shouldHandle, Action<int> onWheel)
        {
            _shouldHandle = shouldHandle;
            _onWheel = onWheel;
        }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = Callback;
            using var module = Process.GetCurrentProcess().MainModule!;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        public void Dispose() => Stop();

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && (int)wParam == WM_MOUSEHWHEEL && _shouldHandle())
            {
                int mouseData = Marshal.ReadInt32(lParam, 8);   // MSLLHOOKSTRUCT.mouseData
                _onWheel((short)(mouseData >> 16));
                return (IntPtr)1;
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);

        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);
    }
}
