using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Navbar.Services
{
    public sealed record AppWindow(IntPtr Handle, string Title, string App, string ExePath, ImageSource? Icon, bool Minimized, bool Active, uint Pid);

    /// <summary>
    /// Fenêtres ouvertes (comme la barre des tâches) : liste tenue à jour par des événements Windows (création, fermeture, titre,
    /// premier plan, réduction), activation, réduction et fermeture. Les fenêtres d'applications lancées en administrateur
    /// ne répondent pas aux commandes (limite de Windows pour une application normale).
    /// </summary>
    public sealed class WindowService
    {
        public event Action? Updated;
        public IReadOnlyList<AppWindow> Windows { get; private set; } = Array.Empty<AppWindow>();

        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
        private readonly Dictionary<IntPtr, int> _order = new();          // ordre d'apparition : la liste reste stable comme une barre des tâches
        private readonly Dictionary<IntPtr, ImageSource?> _icons = new();
        private readonly Dictionary<string, string> _appNames = new();
        private readonly Dictionary<uint, string> _exeByPid = new();
        private readonly List<IntPtr> _hooks = new();
        private WinEventProc? _proc;
        private int _counter;
        private int _consumers;

        public WindowService()
        {
            _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
        }

        /// <summary>Un consommateur (onglet, rangée d'icônes) a besoin de la liste ; les événements ne sont écoutés que tant qu'il y en a.</summary>
        public void Acquire()
        {
            if (_consumers++ > 0) return;
            _proc = OnWinEvent;
            // premier plan, réduction / restauration, création / affichage / masquage / destruction, changement de titre
            foreach (var (min, max) in new[] { (0x0003u, 0x0003u), (0x0016u, 0x0017u), (0x8001u, 0x8003u), (0x800Cu, 0x800Cu) })
                _hooks.Add(SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0, 0x0000 /* WINEVENT_OUTOFCONTEXT */));
            Refresh();
        }

        public void Release()
        {
            if (_consumers == 0 || --_consumers > 0) return;
            foreach (var h in _hooks) UnhookWinEvent(h);
            _hooks.Clear();
            _proc = null;
            _debounce.Stop();
        }

        private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;   // seulement les fenêtres elles-mêmes, pas leurs contrôles
            if (evt != 0x0003 && GetAncestor(hwnd, 2 /* GA_ROOT */) != hwnd) return;
            _debounce.Stop();
            _debounce.Start();
        }

        // ───────────── Énumération ─────────────

#if DEBUG
        private bool _fake;

        /// <summary>Vérification visuelle sans exposer les vraies fenêtres de l'utilisateur : une liste fictive remplace l'énumération.</summary>
        public void SetFakeForTest(IReadOnlyList<AppWindow> list) { _fake = true; Windows = list; Updated?.Invoke(); }
#endif

        public void Refresh()
        {
#if DEBUG
            if (_fake) return;
#endif
            var foreground = GetAncestor(GetForegroundWindow(), 2);
            var found = new List<AppWindow>();
            var seen = new HashSet<IntPtr>();
            uint self = (uint)Environment.ProcessId;

            EnumWindows((h, _) =>
            {
                if (!IsTaskbarWindow(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == self) return true;

                string title = TitleOf(h);
                if (title.Length == 0) return true;

                string exe = ExeOf(pid);
                string app = AppNameOf(exe, title);
                if (!_order.ContainsKey(h)) _order[h] = _counter++;
                if (!_icons.TryGetValue(h, out var icon) || icon == null) _icons[h] = icon = IconOf(h, exe);

                seen.Add(h);
                found.Add(new AppWindow(h, title, app, exe, icon, IsIconic(h), h == foreground, pid));
                return true;
            }, IntPtr.Zero);

            foreach (var gone in _order.Keys.Where(k => !seen.Contains(k)).ToList()) { _order.Remove(gone); _icons.Remove(gone); }
            var sorted = found.OrderBy(w => _order[w.Handle]).ToList();

            // Rien n'a changé : on ne réveille pas l'interface
            if (Same(Windows, sorted)) return;
            Windows = sorted;
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } log)   // effectifs seulement, jamais les titres
                File.AppendAllText(log, "apps=" + string.Join(", ", sorted.Select(w => Path.GetFileNameWithoutExtension(w.ExePath) + "@" + System.Windows.Forms.Screen.FromHandle(w.Handle).DeviceName.Replace(@"\\.\", ""))) + "\n");
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } log3)
                File.AppendAllText(log3, $"windows={sorted.Count} withIcon={sorted.Count(w => w.Icon != null)} minimized={sorted.Count(w => w.Minimized)} active={sorted.Count(w => w.Active)}\n");
#endif
            Updated?.Invoke();
        }

        private static bool Same(IReadOnlyList<AppWindow> a, IReadOnlyList<AppWindow> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Handle != b[i].Handle || a[i].Title != b[i].Title || a[i].Active != b[i].Active || a[i].Minimized != b[i].Minimized) return false;
            return true;
        }

        /// <summary>Fenêtre qui aurait sa place dans la barre des tâches : visible, de premier niveau, ni outil ni cachée (applications Store suspendues).</summary>
        private static bool IsTaskbarWindow(IntPtr h)
        {
            if (!IsWindowVisible(h)) return false;
            int exStyle = GetWindowLong(h, -20);
            bool appWindow = (exStyle & 0x00040000) != 0;     // WS_EX_APPWINDOW
            bool toolWindow = (exStyle & 0x00000080) != 0;    // WS_EX_TOOLWINDOW
            if (toolWindow && !appWindow) return false;
            if (!appWindow && GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return false;   // boîtes de dialogue possédées par une autre fenêtre
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

            var cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
        }

        private static string TitleOf(IntPtr h)
        {
            int length = GetWindowTextLength(h);
            if (length <= 0) return "";
            var text = new StringBuilder(length + 1);
            GetWindowText(h, text, text.Capacity);
            return text.ToString();
        }

        private string ExeOf(uint pid)
        {
            if (_exeByPid.TryGetValue(pid, out var cached)) return cached;
            string path = "";
            var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (process != IntPtr.Zero)
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (QueryFullProcessImageName(process, 0, buffer, ref size)) path = buffer.ToString();
                CloseHandle(process);
            }
            _exeByPid[pid] = path;
            return path;
        }

        private string AppNameOf(string exe, string title)
        {
            if (exe.Length == 0) return title;
            if (_appNames.TryGetValue(exe, out var cached)) return cached;
            string name;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription!.Trim() : Path.GetFileNameWithoutExtension(exe);
            }
            catch { name = Path.GetFileNameWithoutExtension(exe); }
            if (exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) name = title;   // applications du Store : le titre est le meilleur nom
            _appNames[exe] = name;
            return name;
        }

        // ───────────── Icônes ─────────────

        private readonly Dictionary<string, ImageSource?> _exeIcons = new(StringComparer.OrdinalIgnoreCase);

        private ImageSource? IconOf(IntPtr hwnd, string exe)
        {
            // Applications du Store : l'exécutable est ApplicationFrameHost (icône générique), on garde l'icône de la fenêtre
            if (exe.Length > 0 && !exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!_exeIcons.TryGetValue(exe, out var hi)) _exeIcons[exe] = hi = IconExtractor.FromFile(exe);
                if (hi != null) return hi;
            }
            return IconOfWindow(hwnd, exe);
        }

        private ImageSource? IconOfWindow(IntPtr hwnd, string exe)
        {
            IntPtr icon = IntPtr.Zero;
            foreach (int kind in new[] { 1 /* ICON_BIG */, 2 /* ICON_SMALL2 */, 0 /* ICON_SMALL */ })
            {
                if (SendMessageTimeout(hwnd, 0x007F /* WM_GETICON */, (IntPtr)kind, IntPtr.Zero, 0x0002 /* SMTO_ABORTIFHUNG */, 60, out var result) != IntPtr.Zero && result != IntPtr.Zero)
                { icon = result; break; }
            }
            if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, -14 /* GCL_HICON */);

            try
            {
                if (icon != IntPtr.Zero)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                if (exe.Length > 0 && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } fromFile)
                {
                    using (fromFile)
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(fromFile.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        return source;
                    }
                }
            }
            catch { }
            return null;
        }

        // ───────────── Actions ─────────────

        /// <summary>Amène la fenêtre au premier plan ; réduite, elle est d'abord restaurée. Une fenêtre déjà active et visible est réduite (comme la barre des tâches).</summary>
        public void Activate(IntPtr hwnd, bool toggleIfActive = true)
        {
            if (toggleIfActive && !IsIconic(hwnd) && GetAncestor(GetForegroundWindow(), 2) == hwnd) { ShowWindow(hwnd, 6 /* SW_MINIMIZE */); return; }

            if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
            // Windows n'autorise le premier plan qu'à l'application qui vient de recevoir une saisie : une pression sur Alt en donne le droit
            keybd_event(0x12, 0, 0, 0);
            keybd_event(0x12, 0, 2, 0);
            AllowSetForegroundWindow(-1);
            SetForegroundWindow(hwnd);
            Refresh();
        }

        public void Minimize(IntPtr hwnd) { ShowWindow(hwnd, 6); Refresh(); }
        public void Restore(IntPtr hwnd) { ShowWindow(hwnd, 9); Refresh(); }
        public void Maximize(IntPtr hwnd) { ShowWindow(hwnd, 3); Refresh(); }
        public bool IsMaximized(IntPtr hwnd) => IsZoomed(hwnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);

        public void Close(IntPtr hwnd) => PostMessage(hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

        // ───────────── Win32 ─────────────

        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, int flags, int extra);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    }
}





