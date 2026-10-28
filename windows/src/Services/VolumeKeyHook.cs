using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Navbar.Services
{
    /// <summary>
    /// Intercepte les touches volume du clavier pour les traiter nous-mêmes : l'overlay de Windows
    /// ne s'affiche alors plus, seul celui de l'encoche reste.
    /// </summary>
    public sealed class VolumeKeyHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        private const int VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;
        /// <summary>Pas d'une pression de touche (2 % comme Windows par défaut).</summary>
        public float StepSize { get; set; } = 0.02f;

        /// <summary>Traiter les touches volume nous-mêmes (sinon elles passent à Windows).</summary>
        public bool HandleVolumeKeys { get; set; } = true;

        /// <summary>Traiter les touches média (lecture, suivant, précédent) ; l'action est confiée à <see cref="MediaKey"/>.</summary>
        public bool HandleMediaKeys { get; set; }

        /// <summary>Reçoit le code de la touche média pressée (0xB3 lecture/pause, 0xB0 suivant, 0xB1 précédent).</summary>
        public Action<int>? MediaKey { get; set; }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        private readonly VolumeService _volume;
        private HookProc? _proc;            // gardé en champ : le GC ne doit pas le collecter
        private IntPtr _hook;

        public VolumeKeyHook(VolumeService volume) => _volume = volume;

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = Callback;
            using var module = Process.GetCurrentProcess().MainModule!;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
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
            if (code >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);   // KBDLLHOOKSTRUCT.vkCode
                // Les touches média que nous injectons nous-mêmes (VLC…) doivent atteindre Windows, pas boucler ici
                bool injected = (Marshal.ReadInt32(lParam, 8) & 0x10) != 0;   // LLKHF_INJECTED
                if (HandleMediaKeys && !injected && vk is 0xB3 or 0xB0 or 0xB1)
                {
                    int mediaMsg = (int)wParam;
                    if (mediaMsg is WM_KEYDOWN or WM_SYSKEYDOWN) MediaKey?.Invoke(vk);
                    if (mediaMsg is WM_KEYDOWN or WM_SYSKEYDOWN or WM_KEYUP or WM_SYSKEYUP) return (IntPtr)1;
                }
                if (HandleVolumeKeys && vk is VK_VOLUME_UP or VK_VOLUME_DOWN or VK_VOLUME_MUTE)
                {
                    int msg = (int)wParam;
                    if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (vk == VK_VOLUME_UP) _volume.Step(StepSize);
                        else if (vk == VK_VOLUME_DOWN) _volume.Step(-StepSize);
                        else _volume.ToggleMute();
                    }
                    if (msg is WM_KEYDOWN or WM_SYSKEYDOWN or WM_KEYUP or WM_SYSKEYUP)
                        return (IntPtr)1;   // touche consommée : Windows ne la voit pas
                }
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
