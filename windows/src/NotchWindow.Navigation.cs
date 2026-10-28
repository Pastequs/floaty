using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Navbar.Services;

namespace Navbar
{
    // Navigation entre modules : molette horizontale, onglets, et glissement animé d'un module à l'autre
    public partial class NotchWindow
    {
        private MouseHWheelHook? _wheelHook;
        private int _wheelAccumulated;
        private DateTime _lastWheelNav = DateTime.MinValue;

        private void StartWheelHook()
        {
            _wheelHook ??= new MouseHWheelHook(() => _hovered, OnHorizontalWheel);
            _wheelHook.Start();
        }

        private const int WM_MOUSEHWHEEL = 0x020E, WM_MOUSEWHEEL = 0x020A, MK_SHIFT = 0x0004;

        /// <summary>
        /// Certains pilotes de souris envoient la molette horizontale directement à la fenêtre sous le curseur
        /// (sans passer par le hook global) ; d'autres la traduisent en Maj + molette verticale.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEHWHEEL)
            {
                OnHorizontalWheel((short)(((long)wParam >> 16) & 0xFFFF));
                handled = true;
            }
            else if (msg == WM_MOUSEWHEEL && ((long)wParam & MK_SHIFT) != 0 && _view != HoverView.Settings)
            {
                OnHorizontalWheel(-(short)(((long)wParam >> 16) & 0xFFFF));   // Maj + molette vers le bas = vers la droite
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnHorizontalWheel(int delta)
        {
            if (!_hovered) return;

            // Sur le bandeau des rubriques des réglages, la molette horizontale le fait défiler
            if (_view == HoverView.Settings && SettingsPages.IsMouseOver)
            {
                SettingsPages.ScrollToHorizontalOffset(SettingsPages.HorizontalOffset + delta);
                return;
            }

            // Un pavé tactile envoie beaucoup de petits deltas : on cumule et on espace les changements
            _wheelAccumulated += delta;
            if (Math.Abs(_wheelAccumulated) < 120) return;
            if (DateTime.UtcNow - _lastWheelNav < TimeSpan.FromMilliseconds(280)) { _wheelAccumulated = 0; return; }

            NavigateViews(_wheelAccumulated > 0 ? 1 : -1);
            _wheelAccumulated = 0;
            _lastWheelNav = DateTime.UtcNow;
        }

        /// <summary>Onglet de chaque module, dans l'ordre d'affichage de la barre.</summary>
        private (FrameworkElement Tab, HoverView View)[] TabMap()
        {
            var baseMap = TabMapBase();
            var order = _settings.TabOrder;
            if (order.Count == 0) return baseMap;
            // Les modules cités dans l'ordre personnalisé d'abord, les autres ensuite dans l'ordre par défaut
            return baseMap.OrderBy(t => { int i = order.IndexOf(t.View.ToString()); return i >= 0 ? i : 1000 + Array.IndexOf(baseMap, t); }).ToArray();
        }

        private (FrameworkElement Tab, HoverView View)[] TabMapBase() => new (FrameworkElement, HoverView)[]
        {
            (HomeToggle, HoverView.Auto), (WeatherToggle, HoverView.Weather), (MarketToggle, HoverView.Market),
            (SmartToggle, HoverView.Smart), (DiscordToggle, HoverView.Discord), (WinToggle, HoverView.Windows), (ChatToggle, HoverView.Chat),
            (NotesToggle, HoverView.Notes), (SysToggle, HoverView.System), (ClipToggle, HoverView.Clipboard), (ControlsToggle, HoverView.Controls),
            (TasksToggle, HoverView.Tasks), (ShelfToggle, HoverView.Shelf), (TimerToggle, HoverView.Timer),
            (SettingsToggle, HoverView.Settings),
        };

        /// <summary>Ordre des modules : exactement celui des onglets visibles, pour que la molette les parcoure tous un par un.</summary>
        private List<HoverView> ViewOrder()
        {
            var order = new List<HoverView>();
            foreach (var (tab, view) in TabMap())
                if (tab.Visibility == Visibility.Visible) order.Add(view);
            if (order.Count == 0) order.Add(HoverView.Auto);
            return order;
        }

        /// <summary>Répartit la largeur disponible entre les onglets visibles : ils tiennent toujours tous sur la rangée.</summary>
        private void LayoutTabs()
        {
            var visible = TabMap().Where(t => t.Tab.Visibility == Visibility.Visible).ToList();
            if (visible.Count == 0) return;
            double available = (OpenW * _wf - 52) * _scale;
            double width = Math.Min(30 * _scale, available / visible.Count);
            foreach (var (tab, _) in TabMap())
            {
                tab.Margin = new Thickness(0);
                tab.Width = width;
                tab.Height = 24 * _scale;
            }
        }

        /// <summary>Plus de défilement : la rangée affiche tous les onglets.</summary>
        private void ScrollTabIntoView()
        {
            LayoutTabs();
            return;
            var tab = TabMap().FirstOrDefault(t => t.View == _view).Tab;
            if (tab == null || tab.Visibility != Visibility.Visible) return;
            Dispatcher.BeginInvoke(() => tab.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        /// <summary>Passe au module suivant (+1) ou précédent (-1), dans l'ordre des onglets.</summary>
        private void NavigateViews(int direction)
        {
            var order = ViewOrder();
            int i = Math.Max(0, order.IndexOf(TabHome(_view)));
            int next = Math.Clamp(i + direction, 0, order.Count - 1);
            if (next != i) GoTo(order[next]);
        }

        // ───────────── Transition animée ─────────────

        private FrameworkElement ViewElement(HoverView v) => v switch
        {
            HoverView.Timer => TimerView,
            HoverView.Shelf => ShelfView,
            HoverView.Tasks => TasksView,
            HoverView.Settings => SettingsView,
            HoverView.Controls => ControlsView,
            HoverView.Water => WaterView,
            HoverView.Devices => DevicesView,
            HoverView.Search => SearchView,
            HoverView.Clipboard => ClipView,
            HoverView.System => SysView,
            HoverView.Weather => _todayGrid ?? WeatherView,
            HoverView.Notes => NotesView,
            HoverView.Market => MarketView,
            HoverView.Chat => ChatView,
            HoverView.Discord => DiscordView,
            HoverView.Smart => SmartView,
            HoverView.Network => NetView,
            HoverView.Calendar => _todayGrid ?? CalView,
            HoverView.Windows => WinView,
            _ => InCall ? DiscordView : _mediaState.HasMedia ? MediaView : ClockView,
        };

        private void InitNavigation()
        {
            foreach (var el in new FrameworkElement[] { TimerView, ShelfView, TasksView, SettingsView, ControlsView, WaterView, DevicesView, SearchView, ClipView, SysView, WeatherView, NotesView, MarketView, ChatView, DiscordView, SmartView, NetView, CalView, WinView, MediaView, ClockView })
                el.RenderTransform = new TranslateTransform();
        }

        /// <summary>Change de module en glissant : l'ancien part d'un côté pendant que le nouveau arrive de l'autre.</summary>
        private void GoTo(HoverView target)
        {
            if (target == _view) return;

            var order = ViewOrder();
            int from = order.IndexOf(TabHome(_view)), to = order.IndexOf(TabHome(target));
            int direction = from < 0 || to >= from ? 1 : -1;

            var oldElement = ViewElement(_view);
            _view = target;
            if (_view == HoverView.Water) RenderWater();
            if (_view == HoverView.Search) OnSearchOpened();
            if (_view == HoverView.Clipboard) RenderClipboard();
            if (InToday(_view)) { _ = Shared.RefreshWeatherAsync(); RenderCalendar(); _ = Shared.RefreshCalendarAsync(); }
            if (_view == HoverView.Market) _ = Shared.RefreshMarketAsync();
            if (_view == HoverView.System && Shared.LastSystem != null) RenderSystem(Shared.LastSystem);   // dernières valeurs connues, aussitôt
            if (_view == HoverView.Windows) { Shared.Windows.Refresh(); RenderWindows(); }   // état frais à l'ouverture

            if (_view == HoverView.Smart) _ = RefreshSmartAsync();
            ApplyView();

            // La hauteur dépend du module (réglages plus haut) : on la fait glisser avec le contenu
            _smoothResize = true;
            if (_hovered) UpdateState();
            _smoothResize = false;

            var newElement = ViewElement(_view);
            if (ReferenceEquals(oldElement, newElement) || !_hovered) return;

            // Les deux modules sont rendus en bitmap pendant l'animation : le mouvement reste fluide même sur un contenu chargé
            oldElement.CacheMode = new BitmapCache();
            newElement.CacheMode = new BitmapCache();
            _transitionOld = oldElement;

            double distance = 60 * _scale * direction;
            Slide(newElement, distance, 0, 0, 1, 360, 60, new QuinticEase { EasingMode = EasingMode.EaseOut }, () => newElement.CacheMode = null);
            Cascade(newElement, 110);

            // L'ancien module reste visible le temps de sortir
            oldElement.Visibility = Visibility.Visible;
            oldElement.IsHitTestVisible = false;
            Slide(oldElement, 0, -distance * 0.7, 1, 0, 260, 0, new CubicEase { EasingMode = EasingMode.EaseInOut }, () =>
            {
                oldElement.IsHitTestVisible = true;
                oldElement.CacheMode = null;
                // On retire les animations (elles gardent leur dernière valeur) avant de remettre l'élément à neuf
                var shift = (TranslateTransform)oldElement.RenderTransform;
                shift.BeginAnimation(TranslateTransform.XProperty, null);
                oldElement.BeginAnimation(UIElement.OpacityProperty, null);
                shift.X = 0;
                oldElement.Opacity = 1;
                if (ReferenceEquals(_transitionOld, oldElement)) _transitionOld = null;
                if (!ReferenceEquals(oldElement, ViewElement(_view))) oldElement.Visibility = Visibility.Collapsed;
            });
        }

        /// <summary>Apparition en cascade : les blocs du module montent et se fondent l'un après l'autre (titre, puis valeurs, puis boutons).</summary>
        private void Cascade(FrameworkElement view, int baseDelayMs)
        {
            if (view is not System.Windows.Controls.Panel panel) return;
            int n = 0;
            foreach (var child in panel.Children.OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                if (n >= 8) break;
                var shift = child.RenderTransform as TranslateTransform;
                if (shift == null)
                {
                    if (!child.RenderTransform.Value.IsIdentity) continue;   // déjà animé par autre chose (ex. effet d'appui) : on n'y touche pas
                    shift = new TranslateTransform();
                    child.RenderTransform = shift;
                }
                var delay = TimeSpan.FromMilliseconds(baseDelayMs + n * 38);
                n++;

                var rise = new DoubleAnimation(10 * _scale, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = delay, EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } };
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                child.BeginAnimation(UIElement.OpacityProperty, null);
                shift.Y = 10 * _scale;   // position de départ tout de suite, pendant le délai
                child.Opacity = 0;
                var target = child;
                fade.Completed += (_, _) => { target.BeginAnimation(UIElement.OpacityProperty, null); target.Opacity = 1; };
                rise.Completed += (_, _) => { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = 0; };
                shift.BeginAnimation(TranslateTransform.YProperty, rise);
                child.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private static void Slide(FrameworkElement element, double fromX, double toX, double fromOpacity, double toOpacity,
                                  int milliseconds, int delayMs, IEasingFunction ease, Action? done)
        {
            var duration = TimeSpan.FromMilliseconds(milliseconds);
            var delay = TimeSpan.FromMilliseconds(delayMs);

            var move = new DoubleAnimation(fromX, toX, duration) { EasingFunction = ease, BeginTime = delay };
            var fade = new DoubleAnimation(fromOpacity, toOpacity, duration) { EasingFunction = ease, BeginTime = delay };
            if (done != null) move.Completed += (_, _) => done();

            // Position de départ posée tout de suite : sinon l'élément apparaît à sa place finale pendant le délai
            var shift = (TranslateTransform)element.RenderTransform;
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            shift.X = fromX;
            element.Opacity = fromOpacity;

            shift.BeginAnimation(TranslateTransform.XProperty, move);
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}




















