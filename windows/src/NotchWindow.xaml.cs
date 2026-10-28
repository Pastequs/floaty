using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Navbar.Services;
using MediaState = Navbar.Services.MediaState;

namespace Navbar
{
    public partial class NotchWindow : Window
    {
        private readonly double _idleW = 130, _idleH = 14, _idleR = 8;   // taille de départ, avant le premier calcul du contenu choisi
        private const double OpenW = 420, OpenH = 268, OpenRadius = 32, SettingsH = 268;   // +22 : rangée d'onglets sous l'en-tête
        private const double OverlayW = 310, OverlayH = 60, OverlayRadius = 19;

        private readonly double _scale;
        private readonly double _wf = 1, _hf = 1;   // largeur et hauteur de la pastille, indépendantes de l'échelle générale
        private readonly Settings _settings;
        private DispatcherTimer? _clockTimer, _sleepTimer;
        private readonly DispatcherTimer _retractTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
        private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
        private SystemOverlayMonitor _monitor => Shared.Monitor;
        private MediaService _media => Shared.Media;
        private TimerService _timer => Shared.Timer;
        private MediaState _mediaState = MediaState.Empty;
        private readonly string _screenName;
        public string ScreenName => _screenName;
        private enum HoverView { Auto, Timer, Shelf, Tasks, Settings, Controls, Water, Devices, Search, Clipboard, System, Weather, Notes, Market, Chat, Discord, Smart, Network, Calendar, Windows }
        private double _progressRatio;

        // Le rayon des coins est animé avec la taille (sinon les coins redeviennent carrés avant la fin de la fermeture)
        public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
            nameof(Radius), typeof(double), typeof(NotchWindow),
            new PropertyMetadata(0.0, (d, e) =>
            {
                double r = (double)e.NewValue;
                var w = (NotchWindow)d;
                w.Island.CornerRadius = w.CornersFor(r);
                w.GlassSheen.CornerRadius = w.CornersFor(r);
            }));

        public double Radius
        {
            get => (double)GetValue(RadiusProperty);
            set => SetValue(RadiusProperty, value);
        }
        private HoverView _view = HoverView.Auto;

        /// <summary>Module affiché à l'ouverture : l'accueil, ou les fenêtres ouvertes si l'utilisateur l'a choisi.</summary>
        private HoverView DefaultView => _settings.OpenOnWindows && _settings.ShowWindows ? HoverView.Windows : HoverView.Auto;
        private bool _typing;
        private const double PillW = 150, PillH = 30, PillRadius = 15;
        private readonly LyricsService _lyrics = new();
        private IReadOnlyList<LyricLine> _lines = Array.Empty<LyricLine>();
        private string _lyricsKey = "";

        private bool _hovered;
        private bool _smoothResize;

        /// <summary>Hauteur de la pastille ouverte : plus haute pour les modules à listes (réglages, presse-papiers, recherche, tâches).</summary>
        /// <summary>En appel Discord, la vue automatique affiche l'appel (prioritaire sur les médias, comme eux sur l'horloge).</summary>
        private bool InCall => _settings.ShowDiscord && Shared.Discord.Connected && Shared.Discord.InChannel;

        /// <summary>Toutes les pages ont la même hauteur : la pastille ouverte ne change plus de taille d'un onglet à l'autre.</summary>
        private double HeightFor(HoverView view) => OpenH;
        private FrameworkElement? _transitionOld;
        private bool _overlayActive;

        public NotchWindow(Settings settings, string screenName)
        {
            _settings = settings;
            _screenName = screenName;
            _scale = ScreenService.GetScale(ScreenService.Resolve(screenName)) * settings.SizePercent / 100.0;
            _wf = settings.WidthPercent / 100.0;
            _hf = settings.HeightPercent / 100.0;
            _retractTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, settings.RetractMs));
            InitializeComponent();
            InitTheme();
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_EDGE") is { Length: > 0 } testEdge) settings.PillEdge = testEdge;   // vérification visuelle seulement (jamais enregistré)
#endif
            ApplyEdge();

            TimerToggle.Visibility = (settings.ShowTimer || settings.ShowWater) ? Visibility.Visible : Visibility.Collapsed;   // minuteur et eau regroupés
            ShelfToggle.Visibility = settings.ShowShelf ? Visibility.Visible : Visibility.Collapsed;
            TasksToggle.Visibility = settings.ShowTasks ? Visibility.Visible : Visibility.Collapsed;
            WaterToggle.Visibility = Visibility.Collapsed;   // regroupé avec le minuteur
            DevicesToggle.Visibility = Visibility.Collapsed;   // regroupé dans l'onglet « Connexions »
            SearchToggle.Visibility = Visibility.Collapsed;   // la barre de recherche de l'en-tête suffit
            ClipToggle.Visibility = settings.ShowClipboard ? Visibility.Visible : Visibility.Collapsed;
            SysToggle.Visibility = settings.ShowSystem ? Visibility.Visible : Visibility.Collapsed;
            WeatherToggle.Visibility = (settings.ShowWeather || settings.ShowCalendar) ? Visibility.Visible : Visibility.Collapsed;
            NotesToggle.Visibility = settings.ShowNotes ? Visibility.Visible : Visibility.Collapsed;
            MarketToggle.Visibility = settings.ShowMarket ? Visibility.Visible : Visibility.Collapsed;
            ChatToggle.Visibility = settings.ShowChat ? Visibility.Visible : Visibility.Collapsed;
            WinToggle.Visibility = settings.ShowWindows ? Visibility.Visible : Visibility.Collapsed;
            CalToggle.Visibility = Visibility.Collapsed;   // regroupé avec la météo
            NetToggle.Visibility = Visibility.Collapsed;   // regroupé dans l'onglet « Connexions »
            SmartToggle.Visibility = settings.ShowHome ? Visibility.Visible : Visibility.Collapsed;
            DiscordToggle.Visibility = Shared.DiscordVisible ? Visibility.Visible : Visibility.Collapsed;
            _lyricsOpen = settings.LyricsOpenDefault;

            Width = ((OpenW + LyricsExtra) * _wf + 40) * _scale;
            Height = (SettingsH * _hf + 20) * _scale;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0;
            Top = 0;

            Island.Width = _idleW * _wf * _scale;
            Island.Height = _idleH * _hf * _scale;
            Radius = _idleR * _scale;

            OverlayTrack.SizeChanged += (_, _) => OverlayFill.Width = OverlayTrack.ActualWidth * _overlayRatio;
            OverlayPanel.Height = OverlayH * _hf * _scale;
            OverlayPanel.Margin = new Thickness(18 * _scale, 0, 18 * _scale, 0);
            OverlayGlyph.FontSize = 20 * _scale;
            OverlayGlyph.Width = 30 * _scale;
            OverlayTrack.Height = 6 * _scale;
            OverlayTrack.CornerRadius = new CornerRadius(3 * _scale);
            OverlayTrack.Margin = new Thickness(10 * _scale, 0, 14 * _scale, 0);
            OverlayFill.Height = 6 * _scale;
            OverlayFill.CornerRadius = new CornerRadius(3 * _scale);
            OverlayLabel.FontSize = 13 * _scale;
            OverlayLabel.MinWidth = 60 * _scale;

            ClockText.FontSize = 58 * _scale;
            WeekText.FontSize = 16 * _scale;
            HeaderTime.FontSize = 12 * _scale;
            HeaderDate.FontSize = HeaderBattery.FontSize = 11 * _scale;
            HeaderDate.Margin = new Thickness(8 * _scale, 0, 0, 0);
            HeaderBattery.Margin = new Thickness(10 * _scale, 0, 0, 0);
            HeaderBattery.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI, Segoe UI Symbol");
            HeaderBar.Margin = new Thickness(0, -3 * _scale, 0, 0);
            TimeElapsed.FontSize = TimeRemaining.FontSize = 10 * _scale;
            TimeElapsed.Margin = new Thickness(0, 0, 8 * _scale, 0);
            TimeRemaining.Margin = new Thickness(8 * _scale, 0, 0, 0);
            TimeElapsed.MinWidth = TimeRemaining.MinWidth = 32 * _scale;
            TimeRemaining.TextAlignment = TextAlignment.Right;
            ViewHost.Margin = new Thickness(0, 42 * _scale, 0, 0);   // sous l'en-tête et la rangée d'onglets
            foreach (var pt in new[] { PillClock, PillMusicText, PillMusicText2, PillMeeting, PillTimer, PillWater, PillTasks, PillBattery })
            {
                pt.FontSize = 13 * _scale;
                pt.Margin = new Thickness(7 * _scale, 0, 7 * _scale, 0);
            }
            PillMusicClip.Margin = PillMusicText.Margin;
            PillMusicText.Margin = PillMusicText2.Margin = new Thickness(0);
            MascotBox.Width = 120 * _scale; MascotPill.Width = 38 * _scale;
            MascotBox.Height = 68 * _scale; MascotPill.Height = 22 * _scale;
            HoverPanel.Margin = new Thickness(26 * _scale, 14 * _scale, 26 * _scale, 14 * _scale);

            ArtBorder.Width = ArtBorder.Height = 64 * _scale;
            ArtBorder.CornerRadius = new CornerRadius(10 * _scale);
            TitleText.FontSize = 15 * _scale;
            ArtistText.FontSize = 12 * _scale;
            SourceText.FontSize = 10 * _scale;
            ProgressTrack.Height = ProgressFill.Height = 4 * _scale;
            ProgressTrack.CornerRadius = ProgressFill.CornerRadius = new CornerRadius(2 * _scale);
            // La barre est plus étroite quand l'encoche est fermée : on recalcule son remplissage dès qu'elle change de taille
            ProgressTrack.SizeChanged += (_, _) =>
            {
                if (!_seeking) ProgressFill.Width = ProgressTrack.ActualWidth * _progressRatio;
            };
            ProgressHit.Height = 18 * _scale;   // zone cliquable plus haute que la barre elle-même
            ProgressHit.Margin = new Thickness(0, 2 * _scale, 0, 0);
            LyricText.FontSize = 13 * _scale;
            LyricText.Margin = new Thickness(0, 8 * _scale, 0, 0);

            TimerTime.FontSize = 44 * _scale;
            TimerLabel.FontSize = 13 * _scale;
            WaterToggle.Width = WaterToggle.Height = 24 * _scale;
            WaterToggle.Padding = new Thickness(5.5 * _scale, 5.5 * _scale, 5.5 * _scale, 3.5 * _scale);
            foreach (var t in new[] { HomeToggle, SearchToggle, ClipToggle, ControlsToggle, DevicesToggle, TimerToggle, ShelfToggle, TasksToggle, SettingsToggle })
            {
                t.FontSize = 14 * _scale;
                t.Width = t.Height = 24 * _scale;
                t.Padding = new Thickness(0);
            }
            // Tous les onglets tiennent sur une rangée sous l'en-tête : leur largeur s'adapte à leur nombre (voir LayoutTabs)
            TabScroll.HorizontalAlignment = HorizontalAlignment.Left;
            TabScroll.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
            TabScroll.Margin = new Thickness(0, 17 * _scale, 0, 0);
            // La barre d'onglets défile quand il y en a trop pour l'espace à droite de la date : l'onglet actif reste toujours visible
            InitShelf();
            InitTasks();
            InitSettings();
            InitControls();
            InitWater();
            InitLyrics();
            InitDevices();
            InitClockStyle();
            InitNavigation();
            InitPillStyle();
            InitSearch();
            InitClipboardView();
            InitSystemView();
            InitWeather();
            InitNotes();
            InitMarket();
            InitChat();
            InitDiscord();
            InitSmart();
            InitNetwork();
            InitCalendar();
            InitWindows();
            InitToday();
            InitHub();
            InitDynAccent();
            InitGear();
            InitMood();
            InitPrivacy();
            foreach (var b in new[] { TimerPlayBtn, TimerStopBtn, TimerResetBtn })
            {
                b.FontSize = 18 * _scale;
                b.Width = 38 * _scale;
                b.Padding = new Thickness(0, 6 * _scale, 0, 6 * _scale);
            }

            _timer.Changed += OnTimerChanged;
            _timer.Ticked += UpdateTimerText;
            UpdateTimerText();

            _media.Updated += OnMediaUpdated;

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();
            _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _sleepTimer.Tick += (_, _) => CheckSleep();
            _sleepTimer.Start();

            ApplyView();   // couleurs des onglets (accent) dès le départ
            _retractTimer.Tick += (_, _) =>
            {
                _retractTimer.Stop();
                _hovered = Island.IsMouseOver;   // cas où l'encoche a été recréée sous le curseur
                if (_hovered) StartWheelHook(); else _wheelHook?.Stop();
                UpdateState();
            };
            _overlayTimer.Tick += (_, _) => { _overlayTimer.Stop(); _overlayActive = false; UpdateState(); };

            InitEnvironment();
            _monitor.OverlayRequested += ShowOverlay;
            Shared.Overlay += ShowOverlay;
            Shared.DataChanged += OnDataChanged;
            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) =>
            {
                _ready = true;
                UpdateState();   // affiche le contenu choisi dans la pastille fermée
#if DEBUG
                // Aide aux captures d'écran automatiques : ouvre la pastille sur un module sans souris
                if (Environment.GetEnvironmentVariable("NAVBAR_TEST_SHOT") is { Length: > 0 } shot && Enum.TryParse<ShotMode>(shot, out var shotMode))
                    _ = Shared.TakeScreenshotAsync(shotMode);
                if (Environment.GetEnvironmentVariable("NAVBAR_TEST_VIEW") is { Length: > 0 } testView && Enum.TryParse<HoverView>(testView, out var hv))
                {
                    _hovered = true;
                    _view = hv;
                    if (int.TryParse(Environment.GetEnvironmentVariable("NAVBAR_TEST_PAGE"), out int tp)) { _settingsPage = tp; RenderSettings(); }
                    if (hv == HoverView.Search) OnSearchOpened();
                    ApplyView();
                    UpdateState();
                }
#endif
            };
            Closed += (_, _) =>
            {
                // L'application recrée les fenêtres à chaque changement de réglage : on se détache des services partagés
                if (_hovered) System.Threading.Interlocked.Decrement(ref Shared.HoverCount);
                _wheelHook?.Stop();
                _clockTimer?.Stop(); _sleepTimer?.Stop();
                _retractTimer.Stop(); _overlayTimer.Stop();
                Shared.Clipboard.Changed -= OnClipChanged;
                Shared.SystemUpdated -= OnSystemUpdated; Shared.WantSystemStats(this, false);
                _timer.Changed -= OnTimerChanged; _timer.Ticked -= UpdateTimerText;
                _media.Updated -= OnMediaUpdated;
                _monitor.OverlayRequested -= ShowOverlay; _monitor.VolumeChanged -= OnVolumeChanged;
                Shared.Overlay -= ShowOverlay; Shared.DataChanged -= OnDataChanged;
            };
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            // Ne vole jamais le focus et reste hors de la liste Alt+Tab
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            PositionOnScreen();
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }

        private void Island_MouseEnter(object sender, MouseEventArgs e)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_NOHOVER") == "1") return;   // captures automatiques : la vraie souris ne doit pas ouvrir la pastille
#endif
            _pinTimer?.Stop();
            _retractTimer.Stop();
            _hovered = true;
            System.Threading.Interlocked.Increment(ref Shared.HoverCount);
            StartWheelHook();             // le hook global de la souris ne tourne que pendant le survol : il réveille l'application à chaque mouvement
            Shared.Media.FastPolling = true;
            UpdateState();
        }

        private void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            _hovered = false;
            _wheelHook?.Stop();
            System.Threading.Interlocked.Decrement(ref Shared.HoverCount);
            Shared.Media.FastPolling = false;
            if (_pinnedOpen) _pinTimer?.Start();
            _retractTimer.Start();
        }

        /// <summary>Données partagées modifiées dans une autre fenêtre : on réaffiche les listes.</summary>
        private void OnDataChanged(object? sender)
        {
            if (ReferenceEquals(sender, this)) return;
            RenderTasks();
            RenderShelf();
            RenderWater();
            RefreshPill();
        }

        private void ShowOverlay(OverlayInfo info)
        {
            // Dans l'onglet son/luminosité, les curseurs se suffisent : pas d'overlay qui les remplacerait
            if (info.Kind is "volume" or "brightness" && _view == HoverView.Controls && (_hovered || _typing)) return;

            OverlayGlyph.Text = info.Glyph;
            OverlayLabel.Text = info.Label;

            // Les messages sans valeur (Verr Maj, Wi-Fi, casque…) n'ont pas de barre : le texte prend sa place
            bool bar = info.Kind is "" or "volume" or "brightness";
            OverlayTrack.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(OverlayLabel, bar ? 2 : 1);
            Grid.SetColumnSpan(OverlayLabel, bar ? 1 : 2);
            OverlayLabel.TextAlignment = bar ? TextAlignment.Right : TextAlignment.Left;
            OverlayLabel.Margin = new Thickness(bar ? 0 : 10 * _scale, 0, 0, 0);
            UpdateFill(info.Value);

            _overlayActive = true;
            _overlayTimer.Stop();
            _overlayTimer.Start();
            UpdateState();
        }

        private void OnMediaUpdated(MediaState s)
        {
            if (!_settings.ShowMedia) s = MediaState.Empty;
            bool switched = s.HasMedia != _mediaState.HasMedia;
            _mediaState = s;
            RefreshPill();

            ApplyView();
            if (!s.HasMedia) return;

            TitleText.Text = s.Title;
            ArtistText.Text = s.Artist;
            ArtImage.Source = s.Art;
            UpdateArtAccent(s);
            UpdateMascotMood();
            SourceText.Text = s.Source;
            SourceText.ToolTip = null;
            RenderSourceChips(s);
            ProgressHit.Visibility = TimeElapsed.Visibility = TimeRemaining.Visibility = s.CanSeek ? Visibility.Visible : Visibility.Hidden;
            UpdateEq(s.Playing);
            PlayBtn.Text = s.Playing ? "" : "";
            double ratio = s.Duration > 0 ? s.Position / s.Duration : 0;
            _progressRatio = Math.Clamp(ratio, 0, 1);
            TimeElapsed.Text = FormatSpan(s.Position);
            TimeRemaining.Text = s.Duration > 0 ? "-" + FormatSpan(Math.Max(0, s.Duration - s.Position)) : "";
            if (!_seeking) ProgressFill.Width = ProgressTrack.ActualWidth * _progressRatio;

            string key = s.Artist + "|" + s.Title;
            if (key != _lyricsKey)
            {
                _lyricsKey = key;
                _lines = Array.Empty<LyricLine>();
                LyricText.Visibility = Visibility.Collapsed;
                BuildLyricsPane();
                _ = LoadLyricsAsync(key, s);
            }
            UpdateLyric(s.Position);
        }

        private async Task LoadLyricsAsync(string key, MediaState s)
        {
            var lines = await _lyrics.FetchAsync(s.Title, s.Artist, s.Duration);
            if (key != _lyricsKey) return; // la piste a changé entre-temps
            _lines = lines;
            BuildLyricsPane();
            UpdateLyric(_mediaState.Position);
        }

        private void UpdateLyric(double position)
        {
            HighlightLyric(position);
            if (_lines.Count == 0 || !_settings.LyricsLine) { LyricText.Visibility = Visibility.Collapsed; return; }

            string text = "";
            foreach (var l in _lines)
            {
                if (l.Time <= position + 0.3) text = l.Text; else break;
            }
            LyricText.Text = text.Length > 0 ? text : "♪";
            LyricText.Visibility = Visibility.Visible;
        }

        private void ApplyView()
        {
            bool media = _mediaState.HasMedia;
            bool auto = _view == HoverView.Auto;
            TimerView.Visibility = _view == HoverView.Timer ? Visibility.Visible : Visibility.Collapsed;
            ShelfView.Visibility = _view == HoverView.Shelf ? Visibility.Visible : Visibility.Collapsed;
            TasksView.Visibility = _view == HoverView.Tasks ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = _view == HoverView.Settings ? Visibility.Visible : Visibility.Collapsed;
            ControlsView.Visibility = _view == HoverView.Controls ? Visibility.Visible : Visibility.Collapsed;
            WaterView.Visibility = _view == HoverView.Water ? Visibility.Visible : Visibility.Collapsed;
            DevicesView.Visibility = _view == HoverView.Devices ? Visibility.Visible : Visibility.Collapsed;
            DevicesToggle.Foreground = TabBrush(_view == HoverView.Devices);
            SearchView.Visibility = _view == HoverView.Search ? Visibility.Visible : Visibility.Collapsed;
            ClipView.Visibility = _view == HoverView.Clipboard ? Visibility.Visible : Visibility.Collapsed;
            SysView.Visibility = _view == HoverView.System ? Visibility.Visible : Visibility.Collapsed;
            ApplyToday();
            NotesView.Visibility = _view == HoverView.Notes ? Visibility.Visible : Visibility.Collapsed;
            MarketView.Visibility = _view == HoverView.Market ? Visibility.Visible : Visibility.Collapsed;
            ChatView.Visibility = _view == HoverView.Chat ? Visibility.Visible : Visibility.Collapsed;
            UpdateGear();
            ScrollTabIntoView();
            WinView.Visibility = _view == HoverView.Windows ? Visibility.Visible : Visibility.Collapsed;
            WinToggle.Foreground = TabBrush(_view == HoverView.Windows);
            UpdateWindowsAcquire();

            CalToggle.Foreground = TabBrush(_view == HoverView.Calendar);
            NetView.Visibility = _view == HoverView.Network ? Visibility.Visible : Visibility.Collapsed;
            NetToggle.Foreground = TabBrush(_view == HoverView.Network);
            UpdateNetworkSampling();
            SmartView.Visibility = _view == HoverView.Smart ? Visibility.Visible : Visibility.Collapsed;
            SmartToggle.Foreground = TabBrush(_view == HoverView.Smart);
            DiscordView.Visibility = _view == HoverView.Discord || auto && InCall ? Visibility.Visible : Visibility.Collapsed;
            DiscordIcon.Fill = TabBrush(_view == HoverView.Discord);
            MarketIcon.Stroke = TabBrush(_view == HoverView.Market);
            ChatIcon.Stroke = TabBrush(_view == HoverView.Chat);
            WeatherIcon.Stroke = TabBrush(InToday(_view));
            NotesIcon.Stroke = TabBrush(_view == HoverView.Notes);
            SysIcon.Stroke = TabBrush(_view == HoverView.System);
            UpdateSystemSampling();
            ClipToggle.Foreground = TabBrush(_view == HoverView.Clipboard);
            SearchToggle.Foreground = TabBrush(_view == HoverView.Search);
            if (_view == HoverView.Devices) StartDevicesRefresh(); else StopDevicesRefresh();
            WaterDrop.Fill = TabBrush(_view == HoverView.Water);
            LyricsPane.Visibility = LyricsActive ? Visibility.Visible : Visibility.Collapsed;
            MediaView.Margin = new Thickness(0, 0, LyricsActive ? (LyricsExtra - 20) * _scale : 0, 0);
            ControlsToggle.Foreground = TabBrush(InHub(_view));
            UpdateHubBars();
            if (_view == HoverView.Controls) RefreshControls();
            SettingsToggle.Foreground = TabBrush(_view == HoverView.Settings);
            MediaView.Visibility = auto && media && !InCall ? Visibility.Visible : Visibility.Collapsed;
            ClockView.Visibility = auto && !media && !InCall ? Visibility.Visible : Visibility.Collapsed;

            // Les onglets actifs sont en blanc
            HomeToggle.Foreground = TabBrush(auto);
            TimerToggle.Foreground = TabBrush(_view == HoverView.Timer || _view == HoverView.Water);
            ShelfToggle.Foreground = TabBrush(_view == HoverView.Shelf);
            TasksToggle.Foreground = TabBrush(_view == HoverView.Tasks);

            // Pendant une transition, l'ancien module reste affiché jusqu'à la fin de son animation
            if (_transitionOld != null && !ReferenceEquals(_transitionOld, ViewElement(_view)))
                _transitionOld.Visibility = Visibility.Visible;
        }

        // L'onglet actif prend la couleur d'accent choisie
        private System.Windows.Media.Brush TabBrush(bool active) => active ? EffectiveAccent() : FgAlpha(0x99);

        private void SetView(HoverView v)
        {
            GoTo(_view == v ? HoverView.Auto : v);
        }

        private void HomeToggle_Click(object sender, MouseButtonEventArgs e)
        {
            GoTo(HoverView.Auto);
        }

        private void ShelfToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Shelf);
        private void TasksToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Tasks);

        private void AddPreset(string text, Action start)
        {
            var label = new TextBlock
            {
                Text = text, Foreground = Fg(),
                FontSize = 12 * _scale, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI"),
            };
            var chip = new Border
            {
                Child = label, Cursor = Cursors.Hand,
                Background = FgAlpha(0x33),
                CornerRadius = new CornerRadius(10 * _scale),
                Padding = new Thickness(10 * _scale, 4 * _scale, 10 * _scale, 4 * _scale),
                Margin = new Thickness(0, 6 * _scale, 8 * _scale, 0),
            };
            chip.MouseLeftButtonUp += (_, _) => start();
            PresetRow.Children.Add(chip);
        }

        private void TimerToggle_Click(object sender, MouseButtonEventArgs e)
        {
            SetView(HoverView.Timer);
        }

        private void TimerPlay_Click(object sender, MouseButtonEventArgs e) => _timer.TogglePause();
        private void TimerStop_Click(object sender, MouseButtonEventArgs e) => _timer.Stop();
        private void TimerReset_Click(object sender, MouseButtonEventArgs e) => _timer.Reset();

        private void OnTimerChanged()
        {
            UpdateTimerText();
            UpdateState();
        }

        private void UpdateTimerText()
        {
            var r = _timer.Remaining;
            string t = r.TotalHours >= 1 ? r.ToString(@"h\:mm\:ss") : $"{(int)r.TotalMinutes:00}:{r.Seconds:00}";
            TimerTime.Text = t;
            TimerLabel.Text = _timer.Status == TimerStatus.Paused ? _timer.Label + " · en pause" : _timer.Label;
            TimerPlayBtn.Text = _timer.Status == TimerStatus.Running ? "" : "";
            TimerTimeSmall.Text = t;
            TimerLabel2.Text = TimerLabel.Text;
            UpdateTimerRing();
            PillTimer.Text = (_timer.Status == TimerStatus.Paused ? "⏸ " : "") + t;
            RefreshPill();
        }

        private static string FormatSpan(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss");
        }

        private void Play_Click(object sender, MouseButtonEventArgs e)
        {
            PlayBtn.Text = PlayBtn.Text == "" ? "" : "";   // retour visuel immédiat, corrigé au prochain relevé
            _media.TogglePlayPause();
        }

        // Barre de progression : clic ou glisser pour se déplacer dans le média
        private bool _seeking;

        private double SeekRatio(MouseEventArgs e) =>
            Math.Clamp(e.GetPosition(ProgressTrack).X / Math.Max(1, ProgressTrack.ActualWidth), 0, 1);

        private void Progress_Down(object sender, MouseButtonEventArgs e)
        {
            if (_mediaState.Duration <= 0) return;
            _seeking = true;
            ProgressHit.CaptureMouse();
            ProgressFill.Width = ProgressTrack.ActualWidth * SeekRatio(e);
        }

        private void Progress_Move(object sender, MouseEventArgs e)
        {
            if (_seeking) ProgressFill.Width = ProgressTrack.ActualWidth * SeekRatio(e);
        }

        private void Progress_Up(object sender, MouseButtonEventArgs e)
        {
            if (!_seeking) return;
            _seeking = false;
            ProgressHit.ReleaseMouseCapture();
            _media.Seek(_mediaState.Duration * SeekRatio(e));
        }
        private void Next_Click(object sender, MouseButtonEventArgs e) => _media.Next();
        private void Prev_Click(object sender, MouseButtonEventArgs e) => _media.Previous();
        private void Source_Click(object sender, MouseButtonEventArgs e) => _media.NextSource();

        private string FormatTime(DateTime d) => _settings.TimeFormat switch
        {
            "24s" => d.ToString("HH:mm:ss"),
            "12" => d.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture),
            "12s" => d.ToString("h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture),
            _ => d.ToString("HH:mm"),
        };

        private string FormatDate(DateTime d)
        {
            string text = _settings.DateStyle switch
            {
                "long" => d.ToString("dddd d MMMM"),
                "numeric" => d.ToString("dd/MM/yyyy"),
                "none" => "",
                _ => d.ToString("ddd d MMM"),
            };
            return text.Length > 0 ? char.ToUpper(text[0]) + text.Substring(1) : text;
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            string time = FormatTime(now);
            ClockText.Text = time;
            PillClock.Text = time;
            HeaderTime.Text = time;
            HeaderDate.Text = FormatDate(now);
            HeaderDate.Visibility = HeaderDate.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            WeekText.Text = $"Semaine {System.Globalization.ISOWeek.GetWeekOfYear(now)}";
            UpdateClockVisual(now, time);
            MascotBox.SetTimeOfDay(now.Hour);
            MascotPill.SetTimeOfDay(now.Hour);
            RefreshPill();
        }
        private double _idleSeconds;

        // La pastèque s'endort après 5 min sans activité clavier/souris et salue au retour
        private void CheckSleep()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return;
            double idleSeconds = (Environment.TickCount - (int)info.dwTime) / 1000.0;

            _idleSeconds = idleSeconds;
            bool sleep = idleSeconds > (DateTime.Now.Hour is >= 22 or < 5 ? 120 : 300);   // elle s'endort plus vite la nuit
            MascotBox.SetAsleep(sleep);
            MascotPill.SetAsleep(sleep);
            RefreshPill();   // l'heure et la mascotte s'échangent selon l'inactivité
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        private long _cascadeAt;

        private void SetHoverVisible(bool visible)
        {
            HoverPanel.IsHitTestVisible = visible;
            if (visible && HoverPanel.Opacity < 0.3 && Environment.TickCount64 - _cascadeAt > 700 && ViewElement(_view) is { } shown) { _cascadeAt = Environment.TickCount64; Cascade(shown, 170); }   // ouverture : le contenu se pose après le ressort
            HoverPanel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 220 : 100))
                { BeginTime = TimeSpan.FromMilliseconds(visible ? 120 : 0) });
        }

        private double _overlayRatio;

        private void UpdateFill(double value)
        {
            _overlayRatio = Math.Clamp(value, 0, 1);
            OverlayFill.Width = OverlayTrack.ActualWidth * _overlayRatio;   // recalculé aussi quand la piste change de taille
        }

        private void UpdateState()
        {
            DockEach(d => d.Suppress(_hovered || _typing || _pinnedOpen));
            if (_hovered || _typing || _pinnedOpen)
            {
                Animate(OpenWidthNow, HeightFor(_view), OpenRadius, true);
                SetOverlayVisible(_overlayActive);
                SetHoverVisible(!_overlayActive);
            }
            else if (_retractTimer.IsEnabled)
            {
                return; // la souris vient de partir : on attend le délai de rétractation
            }
            else if (_overlayActive)
            {
                Animate(OverlayW, OverlayH, OverlayRadius, true);
                SetOverlayVisible(true);
                SetHoverVisible(false);
            }
            else
            {
                // Pastille fermée : taille et contenu dépendent de ce que l'utilisateur a choisi d'y afficher
                var rest = RestMetrics();
                Animate(rest.W, rest.H, rest.R, false);
                _restApplied = rest;
                SetOverlayVisible(false);
                SetHoverVisible(false);
                SetPillContentVisible(rest.Content);
                _view = DefaultView;
                if (_discordMode != "call") { _discordMode = "call"; RenderDiscord(); }   // la prochaine ouverture montre d'abord les contrôles de l'appel
                ApplyView();
                return;
            }
            SetPillContentVisible(false);
        }

        private void SetOverlayVisible(bool visible)
        {
            OverlayPanel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 220 : 120))
                { BeginTime = TimeSpan.FromMilliseconds(visible ? 90 : 0) });
        }

        private void Animate(double wDip, double hDip, double rDip, bool open)
        {
            // Changement de module déjà ouvert : redimensionnement doux, sans le rebond de l'ouverture
            var ease = _smoothResize ? new QuinticEase { EasingMode = EasingMode.EaseInOut }
                     : open ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.32 }   // ressort
                     : new CubicEase { EasingMode = EasingMode.EaseInOut };
            var dur = TimeSpan.FromMilliseconds(_smoothResize ? 420 : open ? 520 : 340);

            Island.BeginAnimation(WidthProperty, new DoubleAnimation(wDip * _wf * _scale, dur) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(hDip * _hf * _scale, dur) { EasingFunction = ease });
            // Le rayon des coins suit plus calmement que la taille (pas de rebond : un rayon négatif serait invalide)
            BeginAnimation(RadiusProperty, new DoubleAnimation(rDip * _scale, TimeSpan.FromMilliseconds(450))
                           { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}























































