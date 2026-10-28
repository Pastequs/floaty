using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WRect = System.Windows.Shapes.Rectangle;
using Forms = System.Windows.Forms;

namespace Navbar.Services
{
    public enum ShotMode { Screen, Window, Region }

    /// <summary>Captures d'écran : écran entier, fenêtre active ou zone à la souris ; enregistrées en PNG dans Images\Navbar.</summary>
    public static class ScreenshotService
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Navbar");

        /// <summary>Capture selon le mode ; renvoie null si l'utilisateur annule. L'appelant masque l'encoche avant.</summary>
        public static async Task<Bitmap?> CaptureAsync(ShotMode mode)
        {
            var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            switch (mode)
            {
                case ShotMode.Window:
                    var hwnd = GetForegroundWindow();
                    if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) && r.Right - r.Left > 8)
                    {
                        // Les bords invisibles des fenêtres Windows 10/11 sont exclus
                        if (DwmGetWindowAttribute(hwnd, 9, out RECT frame, Marshal.SizeOf<RECT>()) == 0) r = frame;
                        return Grab(new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                    }
                    return Grab(screen.Bounds);
                case ShotMode.Region:
                    using (var full = Grab(screen.Bounds))
                    {
                        var rect = await new RegionSelector(full, screen).PickAsync();
                        if (rect == null) return null;
                        return full.Clone(rect.Value, full.PixelFormat);
                    }
                default:
                    return Grab(screen.Bounds);
            }
        }

        private static Bitmap Grab(Rectangle area)
        {
            var bmp = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
            return bmp;
        }

        public static string Save(Bitmap bitmap)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"Capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        public static BitmapSource ToSource(Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DeleteObject(hBitmap); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

        /// <summary>Fenêtre plein écran figée sur la capture : on tire un rectangle, Échap annule.</summary>
        private sealed class RegionSelector : Window
        {
            private readonly Bitmap _shot;
            private readonly Forms.Screen _screen;
            private readonly TaskCompletionSource<Rectangle?> _result = new();
            private readonly Canvas _canvas = new();
            private readonly WRect[] _dim = { new(), new(), new(), new() };
            private readonly WRect _frame = new() { Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.5 };
            private System.Windows.Point _start;
            private bool _dragging;
            private double _scale = 1;

            public RegionSelector(Bitmap shot, Forms.Screen screen)
            {
                _shot = shot; _screen = screen;
                WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
                Background = System.Windows.Media.Brushes.Black;
                Content = _canvas;
            }

            public Task<Rectangle?> PickAsync()
            {
                SourceInitialized += (_, _) =>
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    var b = _screen.Bounds;
                    SetWindowPos(hwnd, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x0040);
                };
                Loaded += (_, _) =>
                {
                    _scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
                    _canvas.Width = _shot.Width / _scale; _canvas.Height = _shot.Height / _scale;
                    _canvas.Children.Add(new System.Windows.Controls.Image { Source = ToSource(_shot), Width = _canvas.Width, Height = _canvas.Height });
                    foreach (var d in _dim) { d.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0, 0, 0)); _canvas.Children.Add(d); }
                    _canvas.Children.Add(_frame);
                    Layout(new Rect(0, 0, 0, 0), full: true);
                    Activate(); Focus();
                };
                MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(_canvas); _dragging = true; CaptureMouse(); };
                MouseMove += (_, e) => { if (_dragging) Layout(Normalize(_start, e.GetPosition(_canvas)), false); };
                MouseLeftButtonUp += (_, e) =>
                {
                    if (!_dragging) return;
                    _dragging = false; ReleaseMouseCapture();
                    var rect = Normalize(_start, e.GetPosition(_canvas));
                    var px = new Rectangle((int)(rect.X * _scale), (int)(rect.Y * _scale), (int)(rect.Width * _scale), (int)(rect.Height * _scale));
                    px.Intersect(new Rectangle(0, 0, _shot.Width, _shot.Height));
                    Finish(px.Width > 4 && px.Height > 4 ? px : null);
                };
                KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
                MouseRightButtonDown += (_, _) => Finish(null);
                Show();
                return _result.Task;
            }

            private void Finish(Rectangle? rect) { _result.TrySetResult(rect); Close(); }

            private static Rect Normalize(System.Windows.Point a, System.Windows.Point b) =>
                new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

            private void Layout(Rect s, bool full)
            {
                double W = _canvas.Width, H = _canvas.Height;
                if (full) s = new Rect(0, 0, 0, 0);
                Place(_dim[0], 0, 0, W, s.Y);
                Place(_dim[1], 0, s.Bottom, W, Math.Max(0, H - s.Bottom));
                Place(_dim[2], 0, s.Y, s.X, s.Height);
                Place(_dim[3], s.Right, s.Y, Math.Max(0, W - s.Right), s.Height);
                if (full) { Place(_dim[0], 0, 0, W, H); }
                Canvas.SetLeft(_frame, s.X); Canvas.SetTop(_frame, s.Y); _frame.Width = s.Width; _frame.Height = s.Height;
            }

            private static void Place(WRect r, double x, double y, double w, double h)
            {
                Canvas.SetLeft(r, x); Canvas.SetTop(r, y); r.Width = Math.Max(0, w); r.Height = Math.Max(0, h);
            }
        }
    }
}


