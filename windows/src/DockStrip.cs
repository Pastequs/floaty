using System;
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
    /// <summary>
    /// Rangée d'icônes permanente à droite de la pastille fermée : une icône par fenêtre ouverte, un clic l'active.
    /// Fenêtre à part (sans focus, toujours au-dessus) : survoler la rangée n'ouvre pas la pastille.
    /// </summary>
    internal enum DockRole { All, Left, Right }   // Left/Right : la barre est répartie également de part et d'autre de la pastille

    internal sealed class DockStrip : Window
    {
        private readonly DockRole _role;
        private readonly Settings _settings;
        private readonly string _screenName;
        private readonly double _scale;
        private readonly Func<double> _pillWidth;
        private readonly Func<Brush> _accent;
        private readonly Func<Brush> _background;
        private readonly Func<System.Windows.Rect> _pillRect;
        private readonly Func<string> _edge;
        private bool SideEdge => _edge() is "left" or "right";
        private UiKit Kit => new(_scale, _background, () => _fgAlpha(0xFF), _fgAlpha, _accent);
        private readonly Func<byte, Brush> _fgAlpha;
        private readonly Action _openOverflow;
        private readonly StackPanel _icons = new() { Orientation = Orientation.Horizontal };
        private readonly DispatcherTimer _reveal = new() { Interval = TimeSpan.FromMilliseconds(420) };
        private bool _suppressed, _fullscreen, _empty = true;
        /// <summary>Hauteur de la barre en pixels indépendants de l'appareil (0 si masquée) : la pastille fermée s'y aligne.</summary>
        public double BarHeight => IsVisible && !Vertical ? ActualHeight : 0;
        public event Action? BarSizeChanged;
        private double _iconPx;   // taille effective des icônes (réduite seulement si l'écran manque de place)

        /// <summary>Taille choisie, réduite au besoin pour que toutes les icônes tiennent de leur côté de l'écran (jamais de « +n »).</summary>
        private double FitIconSize(int count)
        {
            double wanted = Math.Clamp(_settings.DockIconSize, 12, 36) * _scale;
            if (count == 0) return wanted;

            var screen = ScreenService.Resolve(_screenName);
            double dpi = ScreenService.Dpi(screen);
            double availableDip = Vertical
                ? (SideEdge ? screen.Bounds.Height / 2.0 - 90 * dpi : screen.Bounds.Height - 60 * dpi) / dpi   // colonne : hauteur de l'écran (une moitié si la pastille est au milieu du bord)
                : (screen.Bounds.Width / 2.0 - 150 * _scale * dpi - 24 * dpi) / dpi;        // moitié de l'écran moins la pastille fermée (largeur de référence)
            double perItem = (availableDip - 10 * _scale) / count;                           // padding de la capsule déduit
            double fit = perItem - 9 * _scale;                                               // bouton = icône + 7, marges = 2
            return Math.Max(9 * _scale, Math.Min(wanted, fit));
        }

        private bool LeftOfPill => _role == DockRole.Left || _role == DockRole.All && _settings.DockPlacement == "left";
        private bool Vertical => _settings.WindowsLayout == "vertical" || SideEdge;   // sur un bord latéral, les icônes sont toujours en colonne
        private readonly Border _frame;

        public DockStrip(DockRole role, Settings settings, string screenName, double scale, Func<double> pillWidth, Func<Brush> background, Func<Brush> accent,
                         Func<byte, Brush> fgAlpha, Action openOverflow, Func<System.Windows.Rect> pillRect, Func<string> edge)
        {
            _role = role; _settings = settings; _screenName = screenName; _scale = scale; _pillWidth = pillWidth; _accent = accent; _background = background; _fgAlpha = fgAlpha; _openOverflow = openOverflow; _pillRect = pillRect; _edge = edge;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Focusable = false;
            _frame = new Border { Background = background(), Child = _icons };
            Content = _frame;

            SourceInitialized += (_, _) =>
            {
                // Ne vole jamais le focus et reste hors d'Alt+Tab
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */);
            };
            Action onOrder = () => Dispatcher.BeginInvoke(new Action(Refresh));
            OrderChanged += onOrder;
            Closed += (_, _) => OrderChanged -= onOrder;
            SizeChanged += (_, _) => { Reposition(); BarSizeChanged?.Invoke(); };
            _reveal.Tick += (_, _) => { _reveal.Stop(); _suppressed = false; ApplyVisibility(); };

            Show();
            ApplyVisibility();
            Refresh();
        }

        /// <summary>Conservé pour compatibilité : la barre ne se cache plus quand la pastille s'ouvre, elle se déplace avec elle (voir Reposition).</summary>
        public void Suppress(bool open) { }

        public void SetFullscreen(bool full) { if (_fullscreen == full) return; _fullscreen = full; ApplyVisibility(); }

        private void ApplyVisibility()
        {
            var wanted = !_suppressed && !_fullscreen && !_empty ? Visibility.Visible : Visibility.Hidden;
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } log2)
                System.IO.File.AppendAllText(log2, $"apply wanted={wanted} supp={_suppressed} full={_fullscreen} empty={_empty}\n");
#endif
            if (Visibility != wanted) Visibility = wanted;
            if (wanted == Visibility.Visible) Reposition();
        }

        // ───────────── Contenu ─────────────

        public void Refresh()
        {
            var items = NotchWindow.ItemsFor(_settings, _screenName, dock: true);
            // Autour de la pastille : la moitié (arrondie par excès) à gauche, le reste à droite, dans l'ordre de la barre
            int half = (items.Count + 1) / 2;
            var all = _role == DockRole.All ? items : _role == DockRole.Left ? items.Take(half).ToList() : items.Skip(half).ToList();
            _empty = all.Count == 0;
            _fullKeys = items.Select(i => i.OrderKey).ToList();
            _start = _role == DockRole.Right ? half : 0;
            _icons.Children.Clear();

            // Horizontale : capsule à droite de la pastille ; verticale : colonne le long du bord de l'écran
            _icons.Orientation = Vertical ? Orientation.Vertical : Orientation.Horizontal;
            _frame.CornerRadius = new CornerRadius((Vertical ? 14 : 13) * _scale);
            _frame.Padding = Vertical ? new Thickness(3 * _scale, 5 * _scale, 3 * _scale, 5 * _scale) : new Thickness(5 * _scale, 3 * _scale, 5 * _scale, 3 * _scale);
            if (Vertical) _suppressed = false;

            _iconPx = FitIconSize(all.Count);
            for (int n = 0; n < all.Count; n++) _icons.Children.Add(IconFor(all[n], n));
            ApplyVisibility();
        }

        private System.Collections.Generic.List<string> _fullKeys = new();
        private int _start;
        /// <summary>Déclenché quand l'ordre change : toutes les barres se redessinent.</summary>
        internal static event Action? OrderChanged;

        /// <summary>Déplace l'élément d'index <paramref name="from"/> de cette barre à l'index <paramref name="to"/> et mémorise l'ordre complet.</summary>
        private void Move(int from, int to)
        {
            int count = _icons.Children.Count;
            if (from == to || from < 0 || from >= count || to < 0 || to >= count) return;
            var keys = _fullKeys.ToList();
            string key = keys[_start + from];
            keys.RemoveAt(_start + from);
            keys.Insert(_start + to, key);
            _settings.DockOrder = keys;
            Storage.Save("settings", _settings);
            OrderChanged?.Invoke();
        }

        private UIElement IconFor(TaskbarItem item, int index)
        {
            double iconPx = _iconPx > 0 ? _iconPx : Math.Clamp(_settings.DockIconSize, 12, 36) * _scale;
            double size = iconPx + 7 * _scale;
            double dim = item.Minimized ? 0.5 : item.Running ? 1 : 0.8;   // une épinglée fermée est un peu plus discrète
            var image = new Image { Source = item.Icon, Width = iconPx, Height = iconPx, Opacity = dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var grid = new Grid { Width = size, Height = size };
            grid.Children.Add(image);
            if (item.Running)   // trait sous l'icône : long et coloré pour la fenêtre active, petit point sinon
                grid.Children.Add(new Border
                {
                    Height = 2 * _scale, Width = (item.Active ? 10 : 4) * _scale, CornerRadius = new CornerRadius(1 * _scale),
                    Background = item.Active ? _accent() : _fgAlpha(0x99), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 0.5 * _scale),
                });
            if (item.Windows.Count > 1)   // plusieurs fenêtres : petit compteur
                grid.Children.Add(new TextBlock { Text = item.Windows.Count.ToString(), FontSize = 8 * _scale, Foreground = _fgAlpha(0xDD), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, FontWeight = FontWeights.SemiBold });

            string hint = item.Running
                ? $"{item.Title}\nclic : activer · clic droit : menu · clic milieu : {(item.Pinned ? "nouvelle instance" : "fermer")}"
                : $"{item.Name}\nclic : ouvrir";
            var button = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(8 * _scale), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Margin = Vertical ? new Thickness(0, 1 * _scale, 0, 1 * _scale) : new Thickness(1 * _scale, 0, 1 * _scale, 0), ToolTip = hint,
            };
            button.MouseEnter += (_, _) =>
            {
                button.Background = _fgAlpha(0x26);
                if (_settings.WindowPreview) PreviewWindow.Hover(item, button, Kit, above: false);
            };
            button.MouseLeave += (_, _) =>
            {
                button.Background = Brushes.Transparent;
                if (_settings.WindowPreview) PreviewWindow.Leave();
            };
            Press.Attach(button, 0.88);
            var target = item.Windows.FirstOrDefault(w => w.Active) ?? item.Windows.FirstOrDefault();
            // Glisser-déposer : maintenir le clic gauche et déplacer l'icône le long de la barre pour changer l'ordre
            Point? origin = null; bool dragging = false;
            var shift = new TranslateTransform();
            grid.RenderTransform = shift;
            button.PreviewMouseLeftButtonDown += (_, e) => { origin = e.GetPosition(_icons); dragging = false; };
            button.PreviewMouseMove += (_, e) =>
            {
                if (origin is not { } o || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(_icons);
                double d = Vertical ? p.Y - o.Y : p.X - o.X;
                if (!dragging)
                {
                    if (Math.Abs(d) < 6 * _scale) return;
                    dragging = true;
                    button.CaptureMouse();
                    PreviewWindow.Leave();
                    Panel.SetZIndex(button, 10);
                    grid.Opacity = 0.75;
                }
                if (Vertical) shift.Y = d; else shift.X = d;
            };
            button.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (origin == null) return;
                origin = null;
                if (!dragging) return;
                e.Handled = true;
                button.ReleaseMouseCapture();
                // Nouvelle place : l'icône dont le centre est le plus proche du centre de l'icône déposée
                double drop = Vertical ? shift.Y : shift.X;
                int target = index;
                double best = double.MaxValue;
                for (int n = 0; n < _icons.Children.Count; n++)
                {
                    var c = (FrameworkElement)_icons.Children[n];
                    var top = c.TranslatePoint(new Point(0, 0), _icons);
                    double mid = Vertical ? top.Y + c.ActualHeight / 2 : top.X + c.ActualWidth / 2;
                    var me = button.TranslatePoint(new Point(0, 0), _icons);
                    double myMid = (Vertical ? me.Y + button.ActualHeight / 2 : me.X + button.ActualWidth / 2);   // translation du contenu déjà incluse ? non : le bouton lui-même ne bouge pas
                    double dist = Math.Abs(mid - (myMid + drop));
                    if (dist < best) { best = dist; target = n; }
                }
                shift.X = shift.Y = 0; grid.Opacity = 1;
                dragging = false;
                if (target != index) Move(index, target);
            };
            button.MouseLeftButtonUp += (_, _) => { if (!dragging) Shared.OpenItem(item); };
            button.MouseRightButtonUp += (_, e) => { e.Handled = true; TaskbarMenu.Show(item, Kit); };
            button.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                if (item.Pinned) Shared.LaunchNew(item);
                else if (target != null) Shared.Windows.Close(target.Handle);
            };
            return button;
        }

        // ───────────── Position : à droite de la pastille fermée, en haut de l'écran ─────────────

        public void Reposition()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !IsVisible) return;

            var screen = ScreenService.Resolve(_screenName);
            double dpi = ScreenService.Dpi(screen);
            int x, y;
            string edge = _edge();
            int widthPx = (int)Math.Round(ActualWidth * dpi), heightPx = (int)Math.Round(ActualHeight * dpi);
            if (SideEdge)
            {
                // Pastille au milieu d'un bord latéral : la colonne se place au-dessus (première moitié) ou en dessous de la pastille, le long du même bord
                var pill = _pillRect();
                if (pill.IsEmpty) return;   // la pastille n'est pas encore placée : la prochaine mise à jour s'en chargera
                x = edge == "left" ? screen.Bounds.Left : screen.Bounds.Right - widthPx;
                y = _role == DockRole.Left ? (int)Math.Round(pill.Top - 8 * dpi) - heightPx : (int)Math.Round(pill.Bottom + 8 * dpi);
                y = Math.Clamp(y, screen.Bounds.Top, Math.Max(screen.Bounds.Top, screen.Bounds.Bottom - heightPx));
            }
            else if (Vertical)
            {
                // Colonne centrée verticalement, collée au bord droit ou gauche de l'écran
                x = _settings.DockSide == "left" ? screen.Bounds.Left + (int)Math.Round(6 * dpi) : screen.Bounds.Right - widthPx - (int)Math.Round(6 * dpi);
                y = screen.Bounds.Top + Math.Max(0, (screen.Bounds.Height - heightPx) / 2);
            }
            else
            {
                double centerX = screen.Bounds.Left + screen.Bounds.Width / 2.0;
                x = LeftOfPill
                    ? (int)Math.Round(centerX - _pillWidth() * dpi / 2 - 9 * dpi) - widthPx
                    : (int)Math.Round(centerX + _pillWidth() * dpi / 2 + 9 * dpi);
                // Au ras du bord comme la pastille quand la barre est aussi haute qu'elle ; sinon un petit espace
                int gap = ActualHeight >= 30 * _scale ? 0 : (int)Math.Round(3 * dpi);
                y = edge == "bottom" ? screen.Bounds.Bottom - heightPx - gap : screen.Bounds.Top + gap;
            }            SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0, 0x0001 /* NOSIZE */ | 0x0010 /* NOACTIVATE */);
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } log)
                System.IO.File.AppendAllText(log, $"dock x={x} y={y} size={ActualWidth}x{ActualHeight} pill={_pillWidth()} dpi={dpi} vis={Visibility} supp={_suppressed} full={_fullscreen} empty={_empty}\n");
#endif
        }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}



















