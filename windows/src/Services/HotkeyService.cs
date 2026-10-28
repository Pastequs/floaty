using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Navbar.Services
{
    /// <summary>Raccourci clavier global (Ctrl + Alt + Espace) pour ouvrir ou fermer la pastille sans la souris.</summary>
    public sealed class HotkeyService
    {
        private const int WM_HOTKEY = 0x0312, HotkeyId = 0x4E42;   // « NB »
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000, VK_SPACE = 0x20;

        public event Action? Pressed;

        /// <summary>Combinaisons proposées : nom, modificateurs, touche virtuelle, libellé.</summary>
        public static readonly (string Key, uint Mods, uint Vk, string Label)[] Choices =
        {
            ("ctrl+alt+space", MOD_CONTROL | MOD_ALT, VK_SPACE, "Ctrl+Alt+Espace"),
            ("ctrl+alt+n", MOD_CONTROL | MOD_ALT, 0x4E, "Ctrl+Alt+N"),
            ("ctrl+shift+space", MOD_CONTROL | 0x4, VK_SPACE, "Ctrl+Maj+Espace"),
            ("ctrl+alt+b", MOD_CONTROL | MOD_ALT, 0x42, "Ctrl+Alt+B"),
        };

        private HwndSource? _source;
        public bool Registered { get; private set; }
        public string Label { get; private set; } = "";

        /// <summary>Enregistre la combinaison choisie ; avec « auto », prend la première qui n'est pas déjà utilisée par une autre application.</summary>
        public void Start(string choice)
        {
            Stop();
            _source = new HwndSource(new HwndSourceParameters("NavbarHotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
            _source.AddHook(WndProc);

            var candidates = choice == "auto" ? Choices : Choices.Where(c => c.Key == choice).Concat(Choices);
            foreach (var c in candidates)
            {
                if (RegisterHotKey(_source.Handle, HotkeyId, c.Mods | MOD_NOREPEAT, c.Vk))
                {
                    Registered = true;
                    Label = c.Label;
                    if (choice != "auto" && c.Key != choice) Label += " (la combinaison choisie est déjà prise)";
                    return;
                }
            }
            Registered = false;
            Label = "aucune combinaison libre";
        }

        public void Stop()
        {
            if (_source == null) return;
            UnregisterHotKey(_source.Handle, HotkeyId);
            _source.Dispose();
            _source = null;
            Registered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                Pressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
