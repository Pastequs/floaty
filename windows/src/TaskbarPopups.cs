using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Navbar.Services;

namespace Navbar
{
    /// <summary>Couleurs et taille de l'interface, fournies par la pastille (thème, accent, échelle).</summary>
    internal sealed record UiKit(double Scale, Func<Brush> Bg, Func<Brush> Fg, Func<byte, Brush> Alpha, Func<Brush> Accent);

    /// <summary>
    /// Menu contextuel d'un élément de la barre (comme celui de la barre des tâches Windows) : fenêtres, nouvelle fenêtre,
    /// épingler / détacher (de NOTRE barre : celle de Windows n'est jamais modifiée), restaurer / réduire / agrandir, fermer, emplacement.
    /// Fenêtre à part, sans focus : elle se ferme au premier clic en dehors.
    /// </summary>
    internal sealed class TaskbarMenu : Window
    {
        private static TaskbarMenu? _open;
        private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(70) };

        public static void Show(TaskbarItem item, UiKit ui)
        {
            _open?.Close();
            PreviewWindow.CloseNow();
            _open = new TaskbarMenu(item, ui);
        }

        private TaskbarMenu(TaskbarItem item, UiKit ui)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Focusable = false;

            var stack = new StackPanel { MinWidth = 210 * ui.Scale };
            Content = new Border
            {
                Child = stack, Background = ui.Bg(), BorderBrush = ui.Alpha(0x30), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12 * ui.Scale), Padding = new Thickness(5 * ui.Scale), Margin = new Thickness(6 * ui.Scale),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45 },
            };

            Build(stack, item, ui);

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x00000080);   // sans focus, hors d'Alt+Tab
            };
            Loaded += (_, _) => Place();

            // Fermeture au premier clic hors du menu (la fenêtre ne prend jamais le focus : on surveille la souris)
            _watch.Tick += (_, _) =>
            {
                bool pressed = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;
                if ((pressed && !IsMouseOver) || (GetAsyncKeyState(0x1B) & 0x8000) != 0) Close();
            };
            Closed += (_, _) => { _watch.Stop(); if (ReferenceEquals(_open, this)) _open = null; };
            Show();
            _watch.Start();
        }

        /// <summary>Sous le curseur (la barre est en haut de l'écran), sans sortir de l'écran.</summary>
        private void Place()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            GetCursorPos(out var cursor);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int w = (int)Math.Round(ActualWidth * dpi), h = (int)Math.Round(ActualHeight * dpi);
            int x = Math.Clamp(cursor.X - w / 2, screen.Bounds.Left + 4, screen.Bounds.Right - w - 4);
            int y = Math.Clamp(cursor.Y + (int)(14 * dpi), screen.Bounds.Top + 4, screen.Bounds.Bottom - h - 4);
            SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
        }

        // ───────────── Contenu ─────────────

        private void Build(StackPanel stack, TaskbarItem item, UiKit ui)
        {
            double s = ui.Scale;

            void Header(string text)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8 * s, 4 * s, 8 * s, 5 * s) };
                row.Children.Add(new Image { Source = item.Icon, Width = 16 * s, Height = 16 * s, Margin = new Thickness(0, 0, 8 * s, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = text, Foreground = ui.Alpha(0xAA), FontSize = 11 * s, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 * s });
                stack.Children.Add(row);
            }

            void Separator() => stack.Children.Add(new Border { Height = 1, Background = ui.Alpha(0x22), Margin = new Thickness(6 * s, 4 * s, 6 * s, 4 * s) });

            void Entry(string label, Action action, bool danger = false, string glyph = "")
            {
                var text = new TextBlock { Text = label, Foreground = danger ? new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)) : ui.Fg(), FontSize = 12 * s, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 * s };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22 * s) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11.5 * s, Foreground = ui.Alpha(0x99), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                var card = new Border { Child = row, CornerRadius = new CornerRadius(7 * s), Padding = new Thickness(8 * s, 5 * s, 8 * s, 5 * s), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                card.MouseEnter += (_, _) => card.Background = ui.Alpha(0x24);
                card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
                card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); try { action(); } catch { } };
                stack.Children.Add(card);
            }

            var windows = item.Windows;
            var target = windows.FirstOrDefault(w => w.Active) ?? windows.FirstOrDefault();
            Header(item.Name);

            // Plusieurs fenêtres : on peut choisir laquelle activer
            if (windows.Count > 1)
            {
                foreach (var w in windows.Take(8))
                {
                    var win = w;
                    Entry(w.Title, () => Shared.Windows.Activate(win.Handle, toggleIfActive: false), glyph: w.Active ? "\uE73E" : "\uE737");
                }
                Separator();
            }

            string exe = item.ExePath.Length > 0 ? item.ExePath : (File.Exists(item.LnkPath) && item.LnkPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? item.LnkPath : "");
            if (item.LnkPath.Length > 0 || exe.Length > 0)
                Entry(item.Running ? "Nouvelle fenêtre" : "Ouvrir", () => LaunchNew(item, exe), glyph: "\uE710");

            // Épingler / détacher : seulement dans NOTRE barre (la barre des tâches Windows n'est pas touchée)
            if (item.Pinned)
                Entry("Détacher de la barre", () => Unpin(item), glyph: "\uE77A");
            else if (exe.Length > 0)
                Entry("Épingler à la barre", () => Pin(exe), glyph: "\uE718");

            if (item.Running && target != null)
            {
                Separator();
                if (item.Minimized || target.Minimized) Entry("Restaurer", () => Shared.Windows.Restore(target.Handle), glyph: "\uE73F");
                else if (!Shared.Windows.IsMaximized(target.Handle)) Entry("Agrandir", () => Shared.Windows.Maximize(target.Handle), glyph: "\uE739");
                else Entry("Réduire la taille", () => Shared.Windows.Restore(target.Handle), glyph: "\uE923");
                if (!target.Minimized) Entry("Réduire", () => Shared.Windows.Minimize(target.Handle), glyph: "\uE921");
                Entry(windows.Count > 1 ? "Fermer cette fenêtre" : "Fermer la fenêtre", () => Shared.Windows.Close(target.Handle), danger: true, glyph: "\uE711");
                if (windows.Count > 1) Entry($"Fermer les {windows.Count} fenêtres", () => { foreach (var w in windows) Shared.Windows.Close(w.Handle); }, danger: true, glyph: "\uE894");
            }

            if (exe.Length > 0 && File.Exists(exe))
            {
                Separator();
                Entry("Ouvrir l'emplacement du fichier", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{exe}\"") { UseShellExecute = true }), glyph: "\uE838");
            }
        }

        private static void LaunchNew(TaskbarItem item, string exe)
        {
            string path = item.LnkPath.Length > 0 ? item.LnkPath : exe;
            if (path.Length > 0) try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        private static void Pin(string exe)
        {
            var settings = Shared.Settings;
            if (!settings.ExtraPinned.Contains(exe, StringComparer.OrdinalIgnoreCase)) settings.ExtraPinned.Add(exe);
            Storage.Save("settings", settings);
            Shared.Pinned.Load();
        }

        private static void Unpin(TaskbarItem item)
        {
            var settings = Shared.Settings;
            if (item.Extra) settings.ExtraPinned.RemoveAll(p => p.Equals(item.PinKey, StringComparison.OrdinalIgnoreCase));
            else if (!settings.HiddenPinned.Contains(item.PinKey)) settings.HiddenPinned.Add(item.PinKey);   // masquée chez nous, toujours épinglée dans Windows
            Storage.Save("settings", settings);
            Shared.Pinned.Load();
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }

    /// <summary>
    /// Aperçu des fenêtres d'une application au survol de son icône : miniatures en direct fournies par Windows (DWM),
    /// comme la barre des tâches. Fenêtre sans transparence (les miniatures DWM ne s'affichent pas sur une fenêtre transparente).
    /// </summary>
    internal sealed class PreviewWindow : Window
    {
        private static PreviewWindow? _current;
        private static readonly DispatcherTimer ShowTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
        private static readonly DispatcherTimer HideTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
        private static Action? _pendingShow;

        static PreviewWindow()
        {
            ShowTimer.Tick += (_, _) => { ShowTimer.Stop(); _pendingShow?.Invoke(); _pendingShow = null; };
            HideTimer.Tick += (_, _) => { HideTimer.Stop(); if (_current != null && !_current.IsMouseOver) _current.Close(); };
        }

        /// <summary>Le curseur arrive sur une icône : l'aperçu apparaît après un court délai (pas de clignotement en traversant la barre).</summary>
        public static void Hover(TaskbarItem item, FrameworkElement anchor, UiKit ui, bool above)
        {
            HideTimer.Stop();
            if (!item.Running) return;
            _pendingShow = () => Open(item, anchor, ui);
            if (_current != null) { ShowTimer.Interval = TimeSpan.FromMilliseconds(120); } else ShowTimer.Interval = TimeSpan.FromMilliseconds(380);
            ShowTimer.Stop(); ShowTimer.Start();
        }

        /// <summary>Le curseur quitte l'icône : l'aperçu reste un instant pour permettre d'y aller.</summary>
        public static void Leave()
        {
            ShowTimer.Stop();
            _pendingShow = null;
            HideTimer.Stop(); HideTimer.Start();
        }

        public static void CloseNow() { ShowTimer.Stop(); HideTimer.Stop(); _current?.Close(); }

        private static void Open(TaskbarItem item, FrameworkElement anchor, UiKit ui)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } olog) File.AppendAllText(olog, "preview open called\n");
#endif
            _current?.Close();
            if (!anchor.IsVisible || !anchor.IsMouseOver && _current == null && false) return;
            _current = new PreviewWindow(item, anchor, ui);
        }

        // ───────────── Instance ─────────────

        private readonly List<IntPtr> _thumbs = new();
        private readonly List<(FrameworkElement Region, IntPtr Source)> _regions = new();
        private readonly FrameworkElement _anchor;
        private readonly double _scale;

        private PreviewWindow(TaskbarItem item, FrameworkElement anchor, UiKit ui)
        {
            _anchor = anchor;
            _scale = ui.Scale;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Focusable = false;
            Background = ui.Bg();   // pas de transparence : requis pour les miniatures DWM

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6 * _scale) };
            foreach (var w in item.Windows.Take(5)) row.Children.Add(Card(w, item, ui));
            Content = new Border { Child = row, BorderBrush = ui.Alpha(0x30), BorderThickness = new Thickness(1), Background = ui.Bg() };

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x00000080);
            };
            Loaded += (_, _) => { Place(); RegisterThumbnails(); };
            Closed += (_, _) => { foreach (var t in _thumbs) DwmUnregisterThumbnail(t); _thumbs.Clear(); if (ReferenceEquals(_current, this)) _current = null; };
            MouseLeave += (_, _) => Leave();
            MouseEnter += (_, _) => HideTimer.Stop();
            Show();
        }

        private UIElement Card(AppWindow w, TaskbarItem item, UiKit ui)
        {
            double s = _scale;
            GetWindowRect(w.Handle, out var r);
            double aspect = r.Right - r.Left > 0 && r.Bottom - r.Top > 0 ? (double)(r.Right - r.Left) / (r.Bottom - r.Top) : 1.6;
            double width = 210 * s, height = Math.Clamp(width / aspect, 70 * s, 140 * s);

            var title = new Grid { Margin = new Thickness(0, 0, 0, 4 * s) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.Children.Add(new Image { Source = w.Icon ?? item.Icon, Width = 14 * s, Height = 14 * s, Margin = new Thickness(0, 0, 6 * s, 0) });
            var name = new TextBlock { Text = w.Title, Foreground = ui.Fg(), FontSize = 11 * s, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, MaxWidth = (width - 40 * s) };
            Grid.SetColumn(name, 1);
            title.Children.Add(name);
            var close = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 * s, Foreground = ui.Alpha(0x88), Cursor = Cursors.Hand, Width = 20 * s, TextAlignment = TextAlignment.Center, Background = Brushes.Transparent, ToolTip = "Fermer la fenêtre" };
            close.MouseEnter += (_, _) => close.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            close.MouseLeave += (_, _) => close.Foreground = ui.Alpha(0x88);
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Shared.Windows.Close(w.Handle); Close(); };
            Grid.SetColumn(close, 2);
            title.Children.Add(close);

            // Zone laissée libre : la miniature DWM s'y dessine par-dessus ; fond transparent pour recevoir les clics
            var region = new Border { Width = width, Height = height, Background = Brushes.Transparent, CornerRadius = new CornerRadius(4 * s) };
            if (w.Minimized)
                region.Child = new TextBlock { Text = "Fenêtre réduite", Foreground = ui.Alpha(0x77), FontSize = 11 * s, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI") };
            else _regions.Add((region, w.Handle));

            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(region);
            var card = new Border { Child = stack, Padding = new Thickness(7 * s), Margin = new Thickness(0, 0, 4 * s, 0), CornerRadius = new CornerRadius(8 * s), Cursor = Cursors.Hand, Background = w.Active ? ui.Alpha(0x22) : Brushes.Transparent };
            card.MouseEnter += (_, _) => card.Background = ui.Alpha(0x2E);
            card.MouseLeave += (_, _) => card.Background = w.Active ? ui.Alpha(0x22) : Brushes.Transparent;
            card.MouseLeftButtonUp += (_, _) => { Shared.Windows.Activate(w.Handle, toggleIfActive: false); Close(); };
            return card;
        }

        /// <summary>Sous l'icône survolée, centré, sans sortir de l'écran.</summary>
        private void Place()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var origin = _anchor.PointToScreen(new Point(0, 0));
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)origin.X, (int)origin.Y));
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int w = (int)Math.Round(ActualWidth * dpi), h = (int)Math.Round(ActualHeight * dpi);
            int centerX = (int)(origin.X + _anchor.ActualWidth * dpi / 2);
            int x = Math.Clamp(centerX - w / 2, screen.Bounds.Left + 6, screen.Bounds.Right - w - 6);
            int y = (int)(origin.Y + _anchor.ActualHeight * dpi) + (int)(8 * dpi);
            SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } plog)
                File.AppendAllText(plog, $"preview placed x={x} y={y} size={w}x{h} origin={origin.X},{origin.Y} anchor={_anchor.ActualWidth}x{_anchor.ActualHeight} vis={IsVisible}\n");
#endif
        }

        private void RegisterThumbnails()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            foreach (var (region, source) in _regions)
            {
                if (DwmRegisterThumbnail(hwnd, source, out var thumb) != 0) continue;
                _thumbs.Add(thumb);
                var topLeft = region.TransformToAncestor(this).Transform(new Point(0, 0));
                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = 0x1 | 0x4 | 0x8 | 0x10,   // destination, opacité, visible, zone client seulement
                    rcDestination = new RECT { Left = (int)Math.Round(topLeft.X * dpi), Top = (int)Math.Round(topLeft.Y * dpi), Right = (int)Math.Round((topLeft.X + region.Width) * dpi), Bottom = (int)Math.Round((topLeft.Y + region.Height) * dpi) },
                    opacity = 255, fVisible = 1, fSourceClientAreaOnly = 1,
                };
                DwmUpdateThumbnailProperties(thumb, ref props);
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES { public uint dwFlags; public RECT rcDestination; public RECT rcSource; public byte opacity; public int fVisible; public int fSourceClientAreaOnly; }
        [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}


