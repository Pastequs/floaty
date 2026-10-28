using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;
using Brushes = System.Windows.Media.Brushes;

namespace Navbar
{
    // Barre des tâches alternative : applications épinglées (ouvertes ou non) et fenêtres ouvertes ; rangée d'icônes autour de la pastille en option
    public partial class NotchWindow
    {
        private bool _winAcquired;
        private readonly List<DockStrip> _docks = new();

        /// <summary>Hauteur de la barre la plus haute, en unités de la pastille (0 sans barre horizontale).</summary>
        private double DockBarHeightUnits() => _docks.Count == 0 ? 0 : _docks.Max(d => d.BarHeight) / (_scale * _wf);

        private void DockEach(Action<DockStrip> action) { foreach (var d in _docks.ToList()) action(d); }

        private void InitWindows()
        {
            WinToggle.Width = WinToggle.Height = 24 * _scale;
            WinToggle.Foreground = FgAlpha(0x99);
            WinTitle.FontSize = 14 * _scale;
            WinNote.FontSize = 11 * _scale;
            WinNote.Margin = new Thickness(8 * _scale, 0, 0, 0);
            WinScroll.Margin = new Thickness(0, 6 * _scale, 0, 0);

            Shared.Windows.Updated += OnWindowsUpdated;
            Shared.Pinned.Changed += OnWindowsUpdated;
            Closed += (_, _) =>
            {
                Shared.Windows.Updated -= OnWindowsUpdated;
                Shared.Pinned.Changed -= OnWindowsUpdated;
                if (_winAcquired) { Shared.Windows.Release(); Shared.Pinned.Release(); _winAcquired = false; }
                DockEach(d => d.Close());
            };

#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_FAKEWIN") == "1")
            {
                // Fenêtres fictives (jamais les vraies) pour contrôler l'affichage de la liste et de la rangée d'icônes
                _settings.DockStrip = true;
                if (Environment.GetEnvironmentVariable("NAVBAR_TEST_OPENWIN") == "1") _settings.OpenOnWindows = true;
                if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LAYOUT") is { Length: > 0 } layout) _settings.WindowsLayout = layout;
                if (int.TryParse(Environment.GetEnvironmentVariable("NAVBAR_TEST_ICONSIZE"), out int testSize)) _settings.DockIconSize = testSize;
                if (Environment.GetEnvironmentVariable("NAVBAR_TEST_PLACEMENT") is { Length: > 0 } placement) _settings.DockPlacement = placement;
                var icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(System.Drawing.SystemIcons.Information.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                var fake = Enumerable.Range(1, 12).Select(i => new AppWindow(new IntPtr(1000 + i), $"Document exemple {i}", i % 3 == 0 ? "Éditeur" : i % 3 == 1 ? "Navigateur" : "Terminal", "", icon, i == 4, i == 2, 0)).ToList();
                Shared.Windows.SetFakeForTest(fake);
            }
#endif
            if (_settings.DockStrip)
            {
                Func<Brush> bg = () => (Brush)(TryFindResource("BgBrush") ?? Brushes.Black);
                bool around = _settings.DockPlacement == "around" && (_settings.WindowsLayout != "vertical" || SideEdge);
                if (around)
                {
                    // Autour de la pastille : la barre est répartie également à gauche et à droite
                    _docks.Add(new DockStrip(DockRole.Left, _settings, _screenName, _scale, () => Island.ActualWidth, bg, () => EffectiveAccent(), a => FgAlpha(a), () => OpenPillOn(HoverView.Windows), PillRectPx, () => Edge));
                    _docks.Add(new DockStrip(DockRole.Right, _settings, _screenName, _scale, () => Island.ActualWidth, bg, () => EffectiveAccent(), a => FgAlpha(a), () => OpenPillOn(HoverView.Windows), PillRectPx, () => Edge));
                }
                else
                    _docks.Add(new DockStrip(DockRole.All, _settings, _screenName, _scale, () => Island.ActualWidth, bg, () => EffectiveAccent(), a => FgAlpha(a), () => OpenPillOn(HoverView.Windows), PillRectPx, () => Edge));
            }
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_PREVIEW") is { Length: > 0 } wanted)
            {
                // Vérification de l'aperçu et du menu sur UNE fenêtre de test (Bloc-notes lancé par le test), jamais sur les fenêtres de l'utilisateur
                var probe = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
                probe.Tick += (_, _) =>
                {
                    probe.Stop();
                    if (!ScreenService.Resolve(_screenName).Primary) return;   // un seul écran pour le test (celui qu'on capture)
                    var item = ItemsFor(_settings, _screenName, dock: false).FirstOrDefault(i => i.Running && i.Windows.Any(w => w.App.Contains(wanted, StringComparison.OrdinalIgnoreCase) || w.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase)));
                    if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } plog)
                        System.IO.File.AppendAllText(plog, $"probe wanted={wanted} windows={Shared.Windows.Windows.Count} matched={(item != null)} itemWindows={item?.Windows.Count} dockItemWindows={string.Join("/", ItemsFor(_settings, _screenName, dock: true).Where(i => i.Windows.Count > 1).Select(i => i.Name + ":" + i.Windows.Count))}\n");
                    if (item == null) return;
                    if (Environment.GetEnvironmentVariable("NAVBAR_TEST_MENU") == "1") TaskbarMenu.Show(item, WindowKit());
                    else PreviewWindow.Hover(item, Island, WindowKit(), false);
                };
                probe.Start();
            }
#endif
            // Les barres de part et d'autre suivent la largeur de la pastille quand elle s'ouvre, se referme ou change de contenu
            Island.SizeChanged += (_, _) => DockEach(d => d.Reposition());
            foreach (var dock in _docks) dock.BarSizeChanged += () => Dispatcher.BeginInvoke(RefreshPill);   // la pastille fermée prend la hauteur de la barre
            _view = DefaultView;   // ouverture directe sur les fenêtres si choisi
            UpdateWindowsAcquire();
            RenderWindows();
            ApplyView();
        }

        private UiKit WindowKit() => new(_scale, () => (Brush)(TryFindResource("BgBrush") ?? Brushes.Black), () => Fg(), a => FgAlpha(a), () => EffectiveAccent());

        private void WinToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Windows);

        /// <summary>Les listes ne sont tenues à jour que si l'onglet est affiché ou si la rangée d'icônes est active.</summary>
        private void UpdateWindowsAcquire()
        {
            bool want = _view == HoverView.Windows || _settings.DockStrip || _settings.AccentFromApp;
            if (want && !_winAcquired) { Shared.Windows.Acquire(); Shared.Pinned.Acquire(); _winAcquired = true; }
            else if (!want && _winAcquired) { Shared.Windows.Release(); Shared.Pinned.Release(); _winAcquired = false; }
        }

        private void OnWindowsUpdated() => Dispatcher.BeginInvoke(() =>
        {
            if (_view == HoverView.Windows) RenderWindows();
            if (_settings.AccentFromApp) UpdateDynAccent();
            DockEach(d => d.Refresh());
        });

        /// <summary>Ouvre la pastille sur un module sans la souris (clic sur « +n » de la rangée d'icônes).</summary>
        private void OpenPillOn(HoverView view)
        {
            _pinnedOpen = true;
            _pinTimer?.Stop();
            _pinTimer?.Start();
            _retractTimer.Stop();
            GoTo(view);
            UpdateState();
        }

        /// <summary>Fenêtres à afficher pour cet écran (toutes, ou seulement les siennes selon le réglage et le nombre d'écrans).</summary>
        internal static IEnumerable<AppWindow> WindowsFor(Settings settings, string screenName, bool dock)
        {
            var all = Shared.Windows.Windows;
            // Comme la barre des tâches Windows par défaut : toutes les barres montrent toutes les fenêtres, quel que soit l'écran
            // (une application ouverte sur deux écrans garde ainsi une seule icône avec l'aperçu de toutes ses fenêtres).
            bool filter = settings.WindowsThisScreen;
            if (!filter) return all;
            string device = ScreenService.Resolve(screenName).DeviceName;
            return all.Where(w => w.Pid == 0 || System.Windows.Forms.Screen.FromHandle(w.Handle).DeviceName == device);   // Pid 0 : fenêtres fictives des tests
        }

        /// <summary>Éléments de la barre pour cet écran : épinglées (ouvertes ou non) puis fenêtres non épinglées.</summary>
        internal static List<TaskbarItem> ItemsFor(Settings settings, string screenName, bool dock)
        {
            var items = PinnedAppsService.Merge(Shared.Pinned.Apps, WindowsFor(settings, screenName, dock), settings.ShowPinned);
            if (settings.DockOrder.Count == 0) return items;
            // Ordre choisi par glisser-déposer ; les éléments inconnus (nouvelles fenêtres) restent à la fin, dans leur ordre d'origine
            int Rank(TaskbarItem i) { int r = settings.DockOrder.IndexOf(i.OrderKey); return r < 0 ? int.MaxValue : r; }
            return items.Select((item, n) => (item, n)).OrderBy(t => Rank(t.item)).ThenBy(t => t.n).Select(t => t.item).ToList();
        }

        // ───────────── Liste ─────────────

        private void RenderWindows()
        {
            WinList.Children.Clear();
            var items = ItemsFor(_settings, _screenName, dock: false);
            int running = items.Sum(i => i.Windows.Count);
            WinNote.Text = items.Count == 0 ? "" : $"{running} ouverte(s)" + (items.Any(i => i.Pinned) ? $" · {items.Count(i => i.Pinned)} épinglée(s)" : "");
            if (items.Count == 0) { WinList.Children.Add(Hint("Aucune fenêtre ouverte.")); return; }

            bool tiles = _settings.WindowsLayout == "horizontal";
            var pinned = items.Where(i => i.Pinned).ToList();
            var others = items.Where(i => !i.Pinned).ToList();

            void Section(string title, List<TaskbarItem> list)
            {
                if (list.Count == 0) return;
                if (pinned.Count > 0 && others.Count > 0)
                    WinList.Children.Add(new TextBlock { Text = title, Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont, Margin = new Thickness(2 * _scale, 3 * _scale, 0, 3 * _scale) });
                if (tiles)
                {
                    var wrap = new WrapPanel();
                    foreach (var i in list) wrap.Children.Add(BuildItemTile(i));
                    WinList.Children.Add(wrap);
                }
                else foreach (var i in list) WinList.Children.Add(BuildItemRow(i));
            }
            Section("ÉPINGLÉES", pinned);
            Section("OUVERTES", others);
        }

        /// <summary>Fenêtre sur laquelle agissent « Réduire » et « Fermer » : l'active, sinon la première.</summary>
        private static AppWindow? Target(TaskbarItem item) => item.Windows.FirstOrDefault(w => w.Active) ?? item.Windows.FirstOrDefault();

        private static string Subtitle(TaskbarItem i) =>
            !i.Running ? "Épinglée · clic pour ouvrir"
            : i.Pinned ? (i.Windows.Count > 1 ? $"{i.Windows.Count} fenêtres" : "ouverte") + (i.Minimized ? " · réduite" : "")
            : i.Windows[0].App + (i.Minimized ? " · réduite" : "");

        private void WireItem(Border card, TaskbarItem item, Action<bool> hover)
        {
            card.MouseEnter += (_, _) => hover(true);
            card.MouseLeave += (_, _) => hover(false);
            Press.Attach(card, 0.985);
            card.MouseLeftButtonUp += (_, _) => Shared.OpenItem(item);
            card.MouseRightButtonUp += (_, e) => { e.Handled = true; TaskbarMenu.Show(item, WindowKit()); };
            card.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                if (item.Pinned) Shared.LaunchNew(item);                          // comme la barre des tâches : nouvelle instance
                else if (Target(item) is { } w) Shared.Windows.Close(w.Handle);   // fenêtre non épinglée : fermer
            };
        }

        private UIElement BuildItemRow(TaskbarItem item)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Image { Source = item.Icon, Width = 20 * _scale, Height = 20 * _scale, Margin = new Thickness(0, 0, 9 * _scale, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = item.Minimized ? 0.55 : item.Running ? 1 : 0.85 };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            grid.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Running ? item.Title : item.Name, Foreground = item.Minimized ? FgAlpha(0x88) : Fg(), FontSize = 12 * _scale, FontFamily = UiFont, FontWeight = item.Active ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = Subtitle(item), Foreground = FgAlpha(0x66), FontSize = 10 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            // Boutons réduire / fermer : seulement pour une application ouverte, visibles au survol de la ligne
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0, VerticalAlignment = VerticalAlignment.Center };
            if (item.Running)
            {
                buttons.Children.Add(RowButton("", "Réduire", FgAlpha(0xAA), () => { if (Target(item) is { } w) Shared.Windows.Minimize(w.Handle); }));
                buttons.Children.Add(RowButton("", "Fermer", Down, () => { if (Target(item) is { } w) Shared.Windows.Close(w.Handle); }));
            }
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            var rest = item.Active ? FgAlpha(0x2A) : FgAlpha((byte)(item.Running ? 0x12 : 0x0A));
            var card = new Border
            {
                Child = grid, Background = rest, CornerRadius = new CornerRadius(10 * _scale), Cursor = Cursors.Hand,
                Padding = new Thickness(8 * _scale, 4 * _scale, 6 * _scale, 4 * _scale), Margin = new Thickness(0, 0, 0, 3 * _scale), ToolTip = item.Title,
                BorderBrush = EffectiveAccent(), BorderThickness = new Thickness(item.Active ? 1.2 * _scale : 0),
            };
            WireItem(card, item, on => { card.Background = on ? FgAlpha(0x26) : rest; buttons.Opacity = on ? 1 : 0; });
            return card;
        }

        private UIElement BuildItemTile(TaskbarItem item)
        {
            var icon = new Image { Source = item.Icon, Width = 26 * _scale, Height = 26 * _scale, HorizontalAlignment = HorizontalAlignment.Center, Opacity = item.Minimized ? 0.55 : item.Running ? 1 : 0.85 };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var stack = new StackPanel();
            stack.Children.Add(icon);
            stack.Children.Add(new TextBlock
            {
                Text = item.Running ? item.Title : item.Name, Foreground = item.Minimized ? FgAlpha(0x88) : Fg(), FontSize = 10 * _scale, FontFamily = UiFont, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxHeight = 28 * _scale, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3 * _scale, 0, 0), FontWeight = item.Active ? FontWeights.SemiBold : FontWeights.Normal,   // deux lignes au plus
            });

            var rest = item.Active ? FgAlpha(0x2A) : FgAlpha((byte)(item.Running ? 0x12 : 0x0A));
            var tile = new Border
            {
                Child = stack, Background = rest, CornerRadius = new CornerRadius(10 * _scale), Cursor = Cursors.Hand, Width = 80 * _scale,
                Padding = new Thickness(5 * _scale, 6 * _scale, 5 * _scale, 5 * _scale), Margin = new Thickness(0, 0, 5 * _scale, 5 * _scale),
                BorderBrush = EffectiveAccent(), BorderThickness = new Thickness(item.Active ? 1.2 * _scale : 0),
                ToolTip = $"{item.Title}\n{Subtitle(item)}",
            };
            WireItem(tile, item, on => tile.Background = on ? FgAlpha(0x26) : rest);
            return tile;
        }

        private TextBlock RowButton(string glyph, string tip, Brush hover, Action click)
        {
            var b = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = 11 * _scale, Foreground = FgAlpha(0x88), Width = 24 * _scale, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, ToolTip = tip, Cursor = Cursors.Hand,
            };
            b.MouseEnter += (_, _) => b.Foreground = hover;
            b.MouseLeave += (_, _) => b.Foreground = FgAlpha(0x88);
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
            return b;
        }

        // ───────────── Réglages ─────────────

        private void RenderWindowsSettings()
        {
            var options = new WrapPanel();
            options.Children.Add(Toggle("Applications épinglées à la barre", _settings.ShowPinned, v =>
            {
                _settings.ShowPinned = v;
                Save();
                RenderWindows();
                DockEach(d => d.Refresh());
                RenderSettings();
            }));
            options.Children.Add(Toggle("Aperçu au survol d'une icône", _settings.WindowPreview, v => { _settings.WindowPreview = v; Save(); RenderSettings(); }));
            if (_settings.ShowWindows)
                options.Children.Add(Toggle("Ouvrir la pastille sur les fenêtres", _settings.OpenOnWindows, v =>
                {
                    _settings.OpenOnWindows = v;
                    Save();
                    RenderSettings();
                }));
            options.Children.Add(Toggle("Seulement les fenêtres de cet écran", _settings.WindowsThisScreen, v => { _settings.WindowsThisScreen = v; Save(); RenderWindows(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Liste", options));

            SettingsList.Children.Add(Row("Disposition", ChipsFor(new[] { ("Verticale", "vertical"), ("Horizontale", "horizontal") }, _settings.WindowsLayout, v =>
            {
                _settings.WindowsLayout = v;
                Save();
                if (_settings.DockStrip) { Shared.NotifySettingsChanged(this); RebuildRequested?.Invoke(true); }   // la rangée change de forme avec la pastille
                else { RenderWindows(); RenderSettings(); }
            })));

            // Barre autour de la pastille : option (désactivée par défaut)
            var dock = new WrapPanel();
            dock.Children.Add(Toggle("Barre d'icônes autour de la pastille", _settings.DockStrip, v =>
            {
                _settings.DockStrip = v;
                Save();
                Shared.NotifySettingsChanged(this);
                RebuildRequested?.Invoke(true);   // la barre se crée ou disparaît avec la pastille
            }));
            SettingsList.Children.Add(Row("Barre", dock));

            if (_settings.DockStrip)
            {
                if (_settings.WindowsLayout != "vertical")
                    SettingsList.Children.Add(Row("Position", ChipsFor(new[] { ("À droite", "right"), ("À gauche", "left"), ("Autour (moitié à gauche, moitié à droite)", "around") }, _settings.DockPlacement, v =>
                    {
                        _settings.DockPlacement = v;
                        Save();
                        Shared.NotifySettingsChanged(this);
                        RebuildRequested?.Invoke(true);
                    })));
                else
                    SettingsList.Children.Add(Row("Côté", ChipsFor(new[] { ("Droite", "right"), ("Gauche", "left") }, _settings.DockSide, v =>
                    {
                        _settings.DockSide = v;
                        Save();
                        DockEach(d => d.Reposition());
                        RenderSettings();
                    })));

                SettingsList.Children.Add(Row("Taille", ChipsFor(new[] { ("Petite", 14), ("Moyenne", 17), ("Grande", 21), ("Très grande", 26), ("Géante", 32) }, _settings.DockIconSize, v =>
                {
                    _settings.DockIconSize = v;
                    Save();
                    DockEach(d => { d.Refresh(); d.Reposition(); });
                    RenderSettings();
                })));

            }

            var taskbar = new WrapPanel();
            taskbar.Children.Add(Toggle("Masquer la barre des tâches Windows", _settings.HideTaskbar, v =>
            {
                _settings.HideTaskbar = v;
                Save();
                Shared.ApplyTaskbar();
                RenderSettings();
            }));
            if (_settings.HideTaskbar)
                taskbar.Children.Add(Toggle("La rétablir à la fermeture", _settings.RestoreTaskbarOnExit, v => { _settings.RestoreTaskbarOnExit = v; Save(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Barre Windows", taskbar));

            SettingsList.Children.Add(Row("Utilisation", new TextBlock
            {
                Text = "Les applications épinglées sont celles de ta barre des tâches Windows, dans le même ordre : un clic les ouvre (ou active leur fenêtre, tour à tour s'il y en a plusieurs), " +
                       "un clic milieu en lance une nouvelle instance. Un clic droit ouvre le menu : fenêtres, nouvelle fenêtre, épingler / détacher de CETTE barre (celle de Windows n'est jamais modifiée), restaurer / agrandir / réduire, fermer, ouvrir l'emplacement. " +
                       "Le masquage de la barre Windows est son masquage automatique : elle réapparaît au bord de l'écran. " +
                       "Les fenêtres d'applications lancées en administrateur ne répondent pas aux commandes (limite de Windows).",
                Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));
        }
    }
}












