using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Navbar.Services;

namespace Navbar
{
    // Adaptation à l'environnement : écran choisi, plein écran (jeux, vidéos) et changement d'écran à chaud
    public partial class NotchWindow
    {
        private DispatcherTimer? _fullscreenTimer;
        private double _baseScaleAtStart;

        private void InitEnvironment()
        {
            _baseScaleAtStart = ScreenService.GetScale(ScreenService.Resolve(_screenName));

            _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _fullscreenTimer.Tick += (_, _) => ApplyFullscreenRule();
            _fullscreenTimer.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Closed += (_, _) =>
            {
                _fullscreenTimer?.Stop();
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            };
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                // Si la résolution a changé sensiblement, on recrée l'encoche à la bonne échelle
                if (Math.Abs(ScreenService.GetScale(ScreenService.Resolve(_screenName)) - _baseScaleAtStart) > 0.05)
                {
                    RebuildRequested?.Invoke(false);
                    return;
                }
                PositionOnScreen();
            });
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            PositionOnScreen();   // la fenêtre vient peut-être de changer d'écran, donc de densité
        }

        /// <summary>Centre la fenêtre en haut de l'écran choisi, en pixels (les écrans peuvent avoir des densités différentes).</summary>
        private void PositionOnScreen()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var screen = ScreenService.Resolve(_screenName);
            double dpi = ScreenService.Dpi(screen);
            int w = (int)Math.Round(Width * dpi), h = (int)Math.Round(Height * dpi);
            int x, y;
            switch (Edge)
            {
                case "bottom": x = screen.Bounds.Left + (screen.Bounds.Width - w) / 2; y = screen.Bounds.Bottom - h; break;
                case "left": x = screen.Bounds.Left; y = screen.Bounds.Top + (screen.Bounds.Height - h) / 2; break;
                case "right": x = screen.Bounds.Right - w; y = screen.Bounds.Top + (screen.Bounds.Height - h) / 2; break;
                default: x = screen.Bounds.Left + (screen.Bounds.Width - w) / 2; y = screen.Bounds.Top; break;
            }

            SetWindowPos(hwnd, new IntPtr(-1) /*HWND_TOPMOST*/, x, y, w, h, 0x0010 /*SWP_NOACTIVATE*/);
        }

        private void ApplyFullscreenRule()
        {
            bool full = IsForegroundFullscreenOnMyScreen();
            DockEach(d => d.SetFullscreen(full));
            if (full && Visibility == Visibility.Visible) Visibility = Visibility.Hidden;
            else if (!full && Visibility != Visibility.Visible) Visibility = Visibility.Visible;
        }

        private bool IsForegroundFullscreenOnMyScreen()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            // Le bureau et la barre des tâches couvrent aussi tout l'écran : on les ignore
            var cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            string name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

            // Seul un plein écran sur l'écran de la pastille la masque
            IntPtr fgMonitor = MonitorFromWindow(fg, 2 /*MONITOR_DEFAULTTONEAREST*/);
            var mine = new WindowInteropHelper(this).Handle;
            if (mine != IntPtr.Zero && MonitorFromWindow(mine, 2) != fgMonitor) return false;

            // Une fenêtre agrandie couvre tout l'écran quand la barre des tâches est masquée : ce n'est pas du plein écran
            if (IsZoomed(fg) || (GetWindowLong(fg, -16) & 0x00C00000) != 0) return false;   // WS_CAPTION : fenêtre avec barre de titre

            if (!GetWindowRect(fg, out RECT r)) return false;
            var mon = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(fgMonitor, ref mon)) return false;

            return r.Left <= mon.rcMonitor.Left && r.Top <= mon.rcMonitor.Top &&
                   r.Right >= mon.rcMonitor.Right && r.Bottom >= mon.rcMonitor.Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}






