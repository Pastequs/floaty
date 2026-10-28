using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;
using Color = System.Windows.Media.Color;
using MediaState = Navbar.Services.MediaState;

namespace Navbar
{
    // Réglages accessibles directement depuis l'encoche (onglet engrenage) :
    // un bandeau horizontal de rubriques, une par thème ou par module
    public partial class NotchWindow
    {
        /// <summary>Demande à l'application de recréer l'encoche ; le paramètre dit s'il faut rouvrir les réglages.</summary>
        public event Action<bool>? RebuildRequested;

        private static readonly string[] PageNames =
            { "Apparence", "Pastille", "Heure", "Général", "Média", "Minuteur", "Eau", "Tâches", "Étagère", "Son", "Appareils", "Recherche", "Presse-papiers", "Ressources", "Météo", "Bourse", "Chat IA", "Discord", "Maison", "Réseau", "Agenda", "Fenêtres" };

        private const int PageWater = 6;

        private static readonly (string Label, int Value)[] SizeChoices =
            { ("80 %", 80), ("100 %", 100), ("120 %", 120), ("140 %", 140) };

        private static readonly (string Label, int Value)[] DimensionChoices =
            { ("80 %", 80), ("90 %", 90), ("100 %", 100), ("120 %", 120), ("140 %", 140) };

        private static readonly (string Label, int Value)[] WaterChoices =
            { ("Off", 0), ("30 min", 30), ("45 min", 45), ("1 h", 60), ("90 min", 90) };

        private static readonly (string Label, int Value)[] GoalChoices =
            { ("1 L", 1000), ("1,5 L", 1500), ("2 L", 2000), ("2,5 L", 2500), ("3 L", 3000) };

        private static readonly (string Label, int Value)[] BottleChoices =
            { ("250", 250), ("330", 330), ("500", 500), ("750", 750), ("1 L", 1000) };

        private static readonly (string Label, int Value)[] FromChoices =
            { ("7 h", 7), ("8 h", 8), ("9 h", 9), ("10 h", 10), ("12 h", 12) };

        private static readonly (string Label, int Value)[] ToChoices =
            { ("18 h", 18), ("20 h", 20), ("21 h", 21), ("22 h", 22), ("23 h", 23) };

        private static readonly (string Label, int Value)[] DelayChoices =
            { ("0,3 s", 300), ("0,6 s", 600), ("1 s", 1000), ("2 s", 2000), ("4 s", 4000) };

        private static readonly (string Label, string Value)[] ThemeChoices =
            { ("Sombre", "dark"), ("Clair", "light"), ("Système", "system"), ("Verre", "glass"), ("Nuit", "night"), ("Papier", "paper"), ("Pastèque", "fruit"), ("Néon", "neon"), ("Océan", "ocean"), ("Forêt", "forest"), ("Sakura", "sakura"), ("Terminal", "terminal") };

        private static readonly (string Label, string Value)[] TimeChoices =
            { ("24 h", "24"), ("24 h + s", "24s"), ("12 h", "12"), ("12 h + s", "12s") };

        private static readonly (string Label, string Value)[] DateChoices =
            { ("Longue", "long"), ("Courte", "short"), ("12/03/2026", "numeric"), ("Aucune", "none") };

        private static readonly (string Label, int Value)[] FocusChoices =
            { ("15 min", 15), ("25 min", 25), ("30 min", 30), ("45 min", 45), ("50 min", 50) };

        private static readonly (string Label, int Value)[] BreakChoices =
            { ("3 min", 3), ("5 min", 5), ("10 min", 10), ("15 min", 15) };

        private static readonly (string Label, int Value)[] PresetChoices =
            { ("5", 5), ("10", 10), ("15", 15), ("20", 20), ("30", 30), ("45", 45), ("60", 60) };

        private static readonly (string Label, int Value)[] StepChoices =
            { ("1 %", 1), ("2 %", 2), ("5 %", 5), ("10 %", 10) };

        private static readonly string[] AccentColors =
        {
            "#FFFFFF", "gradient", "#22D3EE", "#60A5FA", "#A78BFA", "#F472B6",
            "#FF4D5E", "#F87171", "#FB923C", "#FACC15", "#A3E635", "#4ADE80",
        };

        private int _settingsPage;

        private void InitSettings()
        {
            SettingsView.Margin = new Thickness(0, 2 * _scale, 0, 0);
            SettingsRule.Margin = new Thickness(0, 2 * _scale, 0, 8 * _scale);
            ApplyAccent();
            ApplyTimerSettings();
            RenderSettings();
        }

        /// <summary>Appelé par l'application après une recréation de l'encoche (changement de taille ou de thème).</summary>
        public void ReopenSettings()
        {
            _view = HoverView.Settings;
            ApplyView();
            _hovered = true;
            UpdateState();
            _retractTimer.Start();   // si la souris n'est plus au-dessus, l'encoche se referme d'elle-même
        }

        private void SettingsToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Settings);

        private void SettingsPages_Wheel(object sender, MouseWheelEventArgs e)
        {
            // La molette verticale fait défiler le bandeau de rubriques à l'horizontale
            SettingsPages.ScrollToHorizontalOffset(SettingsPages.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void OpenSettingsPage(int page)
        {
            _settingsPage = page;
            RenderSettings();
            GoTo(HoverView.Settings);
        }

        private void SettingsPrev_Click(object sender, MouseButtonEventArgs e) =>
            SettingsPages.ScrollToHorizontalOffset(SettingsPages.HorizontalOffset - 160 * _scale);

        private void SettingsNext_Click(object sender, MouseButtonEventArgs e) =>
            SettingsPages.ScrollToHorizontalOffset(SettingsPages.HorizontalOffset + 160 * _scale);

        private void RenderSettings()
        {
            // Bandeau horizontal : une rubrique par thème ou module
            SettingsPagesList.Children.Clear();
            UIElement? activeChip = null;
            for (int i = 0; i < PageNames.Length; i++)
            {
                int page = i;
                var chip = Chip(PageNames[i], i == _settingsPage, () =>
                {
                    _settingsPage = page;
                    RenderSettings();
                });
                if (i == _settingsPage) activeChip = chip;
                SettingsPagesList.Children.Add(chip);
            }
            SettingsPrev.FontSize = SettingsNext.FontSize = 12 * _scale;
            SettingsPrev.Width = SettingsNext.Width = 20 * _scale;
            SettingsPrev.Padding = SettingsNext.Padding = new Thickness(0, 3 * _scale, 0, 0);
            Dispatcher.BeginInvoke(() => (activeChip as FrameworkElement)?.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

            SettingsList.Children.Clear();
            switch (_settingsPage)
            {
                case 0: RenderAppearance(); break;
                case 1: RenderClosedPill(); break;
                case 2: RenderTimeSettings(); break;
                case 3: RenderGeneral(); break;
                case 4: RenderMusicSettings(); break;
                case 5: RenderTimerSettings(); break;
                case PageWater: RenderWaterSettings(); break;
                case 7: RenderTaskSettings(); break;
                case 8: RenderShelfSettings(); break;
                case 9: RenderSoundSettings(); break;
                case 10: RenderDeviceSettings(); break;
                case 11: RenderSearchSettings(); break;
                case 12: RenderClipboardSettings(); break;
                case 13: RenderSystemSettings(); break;
                case 14: RenderWeatherSettings(); break;
                case 15: RenderMarketSettings(); break;
                case 16: RenderChatSettings(); break;
                case 17: RenderDiscordSettings(); break;
                case 18: RenderSmartSettings(); break;
                case 19: RenderNetworkSettings(); break;
                case 20: RenderCalendarSettings(); break;
                default: RenderWindowsSettings(); break;
            }
        }

        private void Save()
        {
            Storage.Save("settings", _settings);
            Shared.NotifySettingsChanged(this);   // les autres écrans se recréent pour refléter le réglage
        }

        // ───────────── Rubriques ─────────────

        private void RenderAppearance()
        {
            SettingsList.Children.Add(Row("Thème", ChipsFor(ThemeChoices, _settings.Theme, v =>
            {
                _settings.Theme = v;
                if (AmbianceAccents.TryGetValue(v, out var accent)) _settings.Accent = accent;   // chaque ambiance apporte son accent (modifiable ensuite)
                Save();
                RebuildRequested?.Invoke(true);
            })));

            var swatches = new WrapPanel();
            foreach (var hex in AccentColors) swatches.Children.Add(Swatch(hex));
            SettingsList.Children.Add(Row("Couleur", swatches));

            var dynamicAccent = new WrapPanel();
            dynamicAccent.Children.Add(Toggle("Suivre la pochette de l'album", _settings.AccentFromArt, v =>
            {
                _settings.AccentFromArt = v;
                Save();
                UpdateArtAccent(_mediaState);
                if (!v) { ApplyAccent(); RefreshPill(); }
                RenderSettings();
            }));
            dynamicAccent.Children.Add(Toggle("Suivre le fond d'écran", _settings.AccentFromWallpaper, v =>
            {
                _settings.AccentFromWallpaper = v;
                Save();
                UpdateDynAccent();
                RenderSettings();
            }));
            dynamicAccent.Children.Add(Toggle("Suivre l'application active", _settings.AccentFromApp, v =>
            {
                _settings.AccentFromApp = v;
                Save();
                UpdateWindowsAcquire();
                UpdateDynAccent();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Accent dynamique", dynamicAccent));

            var mascot = new WrapPanel();
            mascot.Children.Add(Toggle("Réactions aux événements", _settings.MascotReactions, v => { _settings.MascotReactions = v; Save(); }));
            SettingsList.Children.Add(Row("Mascotte", mascot));

            // Plusieurs écrans possibles à la fois ; au moins un reste toujours coché
            var screens = new WrapPanel();
            var active = ActiveScreenNames();
            var all = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < all.Length; i++)
            {
                string name = all[i].DeviceName;
                screens.Children.Add(Toggle(ScreenService.Describe(all[i], i), active.Contains(name), on =>
                {
                    var set = ActiveScreenNames();
                    if (on) set.Add(name); else if (set.Count > 1) set.Remove(name); else return;
                    _settings.Screens = set.ToList();
                    _settings.ScreenName = "";
                    Storage.Save("settings", _settings);
                    RebuildRequested?.Invoke(true);
                }));
            }
            SettingsList.Children.Add(Row("Écrans", screens));

            // Nuance légère du noir (thème sombre) et du blanc (thème clair)
            if (_settings.Theme == "glass")
                SettingsList.Children.Add(Row("Verre", GlassSlider()));
            SettingsList.Children.Add(Row("Noir", ShadeSlider(true)));
            SettingsList.Children.Add(Row("Blanc", ShadeSlider(false)));

            SettingsList.Children.Add(Row("Taille", ChipsFor(SizeChoices, _settings.SizePercent, v =>
            {
                _settings.SizePercent = v;
                Save();
                RebuildRequested?.Invoke(true);
            })));

            SettingsList.Children.Add(Row("Largeur", ChipsFor(DimensionChoices, _settings.WidthPercent, v =>
            {
                _settings.WidthPercent = v;
                Save();
                RebuildRequested?.Invoke(true);
            })));

            SettingsList.Children.Add(Row("Hauteur", ChipsFor(DimensionChoices, _settings.HeightPercent, v =>
            {
                _settings.HeightPercent = v;
                Save();
                RebuildRequested?.Invoke(true);
            })));
        }

        private System.Collections.Generic.HashSet<string> ActiveScreenNames()
        {
            var set = new System.Collections.Generic.HashSet<string>(_settings.Screens.Select(n => ScreenService.Resolve(n).DeviceName));
            if (set.Count == 0) set.Add(ScreenService.Resolve("").DeviceName);
            return set;
        }

        /// <summary>Densité du verre : tout à gauche très transparent, tout à droite presque opaque.</summary>
        private UIElement GlassSlider()
        {
            var row = new SliderRow(_scale, "\uE7B3", EffectiveAccent(), Fg(), FgAlpha(0x33));
            row.Root.Margin = new Thickness(0);
            row.SetValue(_settings.GlassOpacity);
            row.Changing += v =>
            {
                _settings.GlassOpacity = v;
                ApplyShade();
            };
            row.Committed += _ => Save();
            return row.Root;
        }

        private UIElement ShadeSlider(bool dark)
        {
            var row = new SliderRow(_scale, "\uE790", EffectiveAccent(), Fg(), FgAlpha(0x33));
            row.Root.Margin = new Thickness(0);
            row.SetValue(dark ? _settings.ShadeDark : _settings.ShadeLight, dark ? ShadeLabel(_settings.ShadeDark, true) : ShadeLabel(_settings.ShadeLight, false));
            row.Changing += v =>
            {
                if (dark) _settings.ShadeDark = v; else _settings.ShadeLight = v;
                ApplyShade();
            };
            row.Committed += _ => Save();
            return row.Root;
        }

        private static string ShadeLabel(double shade, bool dark) =>
            dark ? (shade < 0.05 ? "Pur" : $"{Math.Round(shade * 100)} %") : (shade < 0.05 ? "Pur" : $"{Math.Round(shade * 100)} %");

        private void RenderClosedPill()
        {
            // « Minimale » exclut tout le reste : la pastille devient un simple trait
            var closed = new WrapPanel();
            closed.Children.Add(PillToggle("Mascotte", _settings.ShowMascot, v => _settings.ShowMascot = v));
            closed.Children.Add(PillToggle("Heure", _settings.PillClock, v => _settings.PillClock = v));
            closed.Children.Add(PillToggle("Média", _settings.PillMusic, v => _settings.PillMusic = v));
            closed.Children.Add(PillToggle("Minuteur", _settings.PillTimer, v => _settings.PillTimer = v));
            closed.Children.Add(PillToggle("Eau", _settings.PillWater, v => _settings.PillWater = v));
            closed.Children.Add(PillToggle("Tâches", _settings.PillTasks, v => _settings.PillTasks = v));
            closed.Children.Add(PillToggle("Batterie", _settings.PillBattery, v => _settings.PillBattery = v));
            closed.Children.Add(PillToggle("Météo", _settings.PillWeather, v => _settings.PillWeather = v));
            closed.Children.Add(PillToggle("Note épinglée", _settings.PillNote, v => _settings.PillNote = v));
            closed.Children.Add(PillToggle("Prochain rendez-vous", _settings.PillCalendar, v => _settings.PillCalendar = v));
            closed.Children.Add(PillToggle("Bourse", _settings.PillMarket, v => _settings.PillMarket = v));
            closed.Children.Add(PillToggle("CPU et RAM", _settings.PillSystem, v =>
            {
                _settings.PillSystem = v;
                Shared.WantSystemStats("pill", v);
            }));
            SettingsList.Children.Add(Row("Contenu", closed));

            var minimal = new WrapPanel();
            minimal.Children.Add(Toggle("Pastille minimale", _settings.MinimalNotch, v =>
            {
                _settings.MinimalNotch = v;
                if (v)
                {
                    _settings.ShowMascot = _settings.PillClock = _settings.PillMusic = false;
                    _settings.PillTimer = _settings.PillWater = _settings.PillTasks = _settings.PillBattery = false;
                    _settings.PillWeather = _settings.PillMarket = _settings.PillSystem = _settings.PillCalendar = _settings.PillNote = false;
                    Shared.WantSystemStats("pill", false);
                }
                AfterPillChange();
            }));
            minimal.Children.Add(Toggle("Titre à côté des barres", _settings.PillMusicTitle, v =>
            {
                _settings.PillMusicTitle = v;
                AfterPillChange();
                RenderSettings();
            }));
            if (_settings.PillMusicTitle)
            minimal.Children.Add(Toggle("Titre média défilant", _settings.PillMarquee, v =>
            {
                _settings.PillMarquee = v;
                AfterPillChange();
            }));
            minimal.Children.Add(Toggle("Mascotte à l'inactivité", _settings.PillSwap, v =>
            {
                _settings.PillSwap = v;
                AfterPillChange();
            }));
            SettingsList.Children.Add(Row("Style", minimal));

            SettingsList.Children.Add(Row("Bord de l'écran", ChipsFor(new[] { ("Haut", "top"), ("Bas", "bottom"), ("Gauche", "left"), ("Droite", "right") }, _settings.PillEdge, v =>
            {
                _settings.PillEdge = v;
                Save();
                Shared.NotifySettingsChanged(this);
                RebuildRequested?.Invoke(true);   // la pastille et ses barres se recréent au bord choisi
            })));

            SettingsList.Children.Add(Row("Fermeture", ChipsFor(DelayChoices, _settings.RetractMs, v =>
            {
                _settings.RetractMs = v;
                Save();
                _retractTimer.Interval = TimeSpan.FromMilliseconds(v);
                RenderSettings();
            })));
        }

        private UIElement PillToggle(string label, bool on, Action<bool> set) =>
            Toggle(label, on, v =>
            {
                set(v);
                if (v) _settings.MinimalNotch = false;   // cocher un contenu sort du mode minimal
                AfterPillChange();
            });

        private void AfterPillChange()
        {
            Save();
            RenderSettings();
            if (!_hovered) UpdateState();
        }

        private void RenderTimeSettings()
        {
            SettingsList.Children.Add(Row("Style", ChipsFor(ClockStyles, _settings.ClockStyle, v =>
            {
                _settings.ClockStyle = v;
                Save();
                ApplyClockStyle();
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Heure", ChipsFor(TimeChoices, _settings.TimeFormat, v =>
            {
                _settings.TimeFormat = v;
                Save();
                UpdateClock();
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Date", ChipsFor(DateChoices, _settings.DateStyle, v =>
            {
                _settings.DateStyle = v;
                Save();
                UpdateClock();
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Aperçu", BuildClockPreview()));
        }

        private void RenderGeneral()
        {
            var modules = new WrapPanel();
            modules.Children.Add(Toggle("Média", _settings.ShowMedia, v => { _settings.ShowMedia = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Overlays", _settings.ShowOverlays, v => { _settings.ShowOverlays = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Appareils", _settings.ShowDevices, v => { _settings.ShowDevices = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Recherche", _settings.ShowSearch, v => { _settings.ShowSearch = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Météo", _settings.ShowWeather, v => { _settings.ShowWeather = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Notes", _settings.ShowNotes, v => { _settings.ShowNotes = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Bourse", _settings.ShowMarket, v => { _settings.ShowMarket = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Chat IA", _settings.ShowChat, v => { _settings.ShowChat = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Fenêtres", _settings.ShowWindows, v => { _settings.ShowWindows = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Agenda", _settings.ShowCalendar, v => { _settings.ShowCalendar = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Réseau", _settings.ShowNetwork, v => { _settings.ShowNetwork = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Maison", _settings.ShowHome, v => { _settings.ShowHome = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Discord", _settings.ShowDiscord, v => { _settings.ShowDiscord = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Ressources", _settings.ShowSystem, v => { _settings.ShowSystem = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Presse-papiers", _settings.ShowClipboard, v => { _settings.ShowClipboard = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Eau", _settings.ShowWater, v => { _settings.ShowWater = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Minuteur", _settings.ShowTimer, v => { _settings.ShowTimer = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Étagère", _settings.ShowShelf, v => { _settings.ShowShelf = v; ApplyModules(); }));
            modules.Children.Add(Toggle("Tâches", _settings.ShowTasks, v => { _settings.ShowTasks = v; ApplyModules(); }));
            SettingsList.Children.Add(Row("Modules", modules));

            var quiet = new WrapPanel();
            quiet.Children.Add(Toggle("Ne pas déranger", _settings.QuietManual, v => { _settings.QuietManual = v; Save(); RenderSettings(); }));
            quiet.Children.Add(Toggle("Mode réunion automatique", _settings.MeetingMode, v => { _settings.MeetingMode = v; Save(); Shared.ApplyServices(); RenderSettings(); }));
            quiet.Children.Add(Toggle("Pendant un Pomodoro Focus", _settings.QuietWithFocus, v => { _settings.QuietWithFocus = v; Save(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Concentration", quiet));

            var notifs = new WrapPanel();
            notifs.Children.Add(Toggle("Aperçu des notifications", _settings.ShowNotifications, v => { _settings.ShowNotifications = v; Save(); Shared.ApplyServices(); RenderSettings(); }));
            if (_settings.ShowNotifications)
                notifs.Children.Add(Toggle("Masquer le contenu", _settings.NotificationsHideContent, v => { _settings.NotificationsHideContent = v; Save(); Shared.ApplyServices(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Notifications", notifs));
            if (_settings.ShowNotifications)
                SettingsList.Children.Add(Row("", new TextBlock
                {
                    Text = Shared.Notifications.Status + ". Les notifications ne sont ni enregistrées ni envoyées : seule l'application (et le texte, sauf si masqué) s'affiche un instant dans la pastille.",
                    Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                }));

            RenderTabOrderSettings();
            RenderProfileSettings();

            // Combinaison du raccourci : « Auto » prend la première libre
            if (_settings.HotkeyEnabled)
            {
                var keys = new WrapPanel();
                keys.Children.Add(Chip("Auto", _settings.HotkeyChoice == "auto", () => ChooseHotkey("auto")));
                foreach (var c in HotkeyService.Choices)
                {
                    string key = c.Key;
                    keys.Children.Add(Chip(c.Label, _settings.HotkeyChoice == key, () => ChooseHotkey(key)));
                }
                SettingsList.Children.Add(Row("Raccourci", keys));
            }

            var misc = new WrapPanel();
            misc.Children.Add(Toggle($"Raccourci {(_settings.HotkeyEnabled && Shared.Hotkey.Registered ? Shared.Hotkey.Label : "clavier")}", _settings.HotkeyEnabled, v =>
            {
                _settings.HotkeyEnabled = v;
                Save();
                Shared.ApplyServices();
                RenderSettings();
            }));
            misc.Children.Add(Toggle("Pastilles micro / caméra", _settings.PrivacyDots, v =>
            {
                _settings.PrivacyDots = v;
                Save();
                Shared.ApplyServices();
                RenderSettings();
                RefreshPill();
            }));
            misc.Children.Add(Toggle("Lancer avec Windows", AutoStart.IsEnabled, v => { AutoStart.Set(v); RenderSettings(); }));
            misc.Children.Add(Chip("À propos", false, ShowAbout));
            SettingsList.Children.Add(Row("Langue", ChipsFor(new[] { ("Auto", "auto"), ("Français", "fr"), ("English", "en") }, _settings.Language, v =>
            {
                _settings.Language = v;
                Save();
                Loc.Init(v);
                RebuildRequested?.Invoke(true);   // les fenêtres sont recréées dans la nouvelle langue
            })));
            misc.Children.Add(Chip("Quitter", false, () => Application.Current.Shutdown()));
            SettingsList.Children.Add(Row("Système", misc));
        }

        private static readonly (string Label, string Kind)[] MediaSources =
        {
            ("VLC", "vlc"), ("Lecteur multimédia", "wmp"), ("Apple Music", "apple"), ("Spotify", "spotify"),
            ("Chrome / Brave", "chrome"), ("Edge", "edge"), ("Firefox / Zen", "firefox"), ("YouTube", "youtube"), ("Autres", "other"),
        };

        private void ChooseHotkey(string key)
        {
            _settings.HotkeyChoice = key;
            Save();
            Shared.ApplyServices();
            RenderSettings();
            Shared.ShowEverywhere(new OverlayInfo("\uE765", 1, Shared.Hotkey.Registered ? "Raccourci : " + Shared.Hotkey.Label : "Raccourci indisponible", "privacy"));
        }

        private void RenderMusicSettings()
        {
            // Sources prises en compte : chacune peut être ignorée
            var sources = new WrapPanel();
            foreach (var (label, kind) in MediaSources)
            {
                string k = kind;
                sources.Children.Add(Toggle(label, !_settings.DisabledMedia.Contains(k), on =>
                {
                    if (on) _settings.DisabledMedia.Remove(k);
                    else if (!_settings.DisabledMedia.Contains(k)) _settings.DisabledMedia.Add(k);
                    Save();
                    RenderSettings();
                }));
            }
            SettingsList.Children.Add(Row("Sources", sources));

            // Quand plusieurs lecteurs jouent en même temps, celui-ci passe en premier
            var preferred = new WrapPanel();
            preferred.Children.Add(Chip("Automatique", _settings.PreferredMedia.Length == 0, () => { _settings.PreferredMedia = ""; Save(); Shared.ApplyServices(); RenderSettings(); }));
            foreach (var (label, kind) in MediaSources.Where(m => m.Kind != "youtube" && m.Kind != "other"))
            {
                var k = kind;
                preferred.Children.Add(Chip(label, _settings.PreferredMedia == k, () => { _settings.PreferredMedia = k; Save(); Shared.ApplyServices(); RenderSettings(); }));
            }
            SettingsList.Children.Add(Row("Prioritaire", preferred));

            var lyrics = new WrapPanel();
            lyrics.Children.Add(Toggle("Ligne sous le titre", _settings.LyricsLine, v =>
            {
                _settings.LyricsLine = v;
                Save();
                UpdateLyric(_mediaState.Position);
                RenderSettings();
            }));
            lyrics.Children.Add(Toggle("Ouvertes par défaut", _settings.LyricsOpenDefault, v =>
            {
                _settings.LyricsOpenDefault = v;
                Save();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Paroles", lyrics));
        }

        private void RenderTimerSettings()
        {
            SettingsList.Children.Add(Row("Focus", ChipsFor(FocusChoices, _settings.PomoFocusMin, v =>
            {
                _settings.PomoFocusMin = v;
                TimerSettingsChanged();
            })));

            SettingsList.Children.Add(Row("Pause", ChipsFor(BreakChoices, _settings.PomoBreakMin, v =>
            {
                _settings.PomoBreakMin = v;
                TimerSettingsChanged();
            })));

            SettingsList.Children.Add(Row("Raccourci 1", ChipsFor(PresetChoices, _settings.PresetA, v =>
            {
                _settings.PresetA = v;
                TimerSettingsChanged();
            })));

            SettingsList.Children.Add(Row("Raccourci 2", ChipsFor(PresetChoices, _settings.PresetB, v =>
            {
                _settings.PresetB = v;
                TimerSettingsChanged();
            })));

            var sound = new WrapPanel();
            sound.Children.Add(Toggle("Son de fin", _settings.TimerSound, v => { _settings.TimerSound = v; Save(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Options", sound));
        }

        private void TimerSettingsChanged()
        {
            Save();
            ApplyTimerSettings();
            RenderSettings();
        }

        /// <summary>Applique durées et raccourcis du minuteur (les boutons de raccourci sont reconstruits).</summary>
        private void ApplyTimerSettings()
        {
            _timer.FocusMinutes = _settings.PomoFocusMin;
            _timer.BreakMinutes = _settings.PomoBreakMin;

            PresetRow.Children.Clear();
            int a = _settings.PresetA, b = _settings.PresetB;
            AddPreset($"{a} min", () => _timer.StartMinutes(a));
            AddPreset($"{b} min", () => _timer.StartMinutes(b));
            AddPreset("Pomodoro", () => _timer.StartPomodoro());
        }

        private void RenderWaterSettings()
        {
            SettingsList.Children.Add(Row("Objectif", ChipsFor(GoalChoices, _settings.WaterGoalMl, v =>
            {
                _settings.WaterGoalMl = v;
                Save();
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Bouteille", ChipsFor(BottleChoices, _settings.BottleMl, v =>
            {
                _settings.BottleMl = v;
                Save();
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Rappel", ChipsFor(WaterChoices, _settings.WaterMinutes, v =>
            {
                _settings.WaterMinutes = v;
                Save();
                Shared.SetWaterInterval(v);
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Dès", ChipsFor(FromChoices, _settings.WaterFromHour, v =>
            {
                _settings.WaterFromHour = Math.Min(v, _settings.WaterToHour - 1);
                Save();
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Jusqu'à", ChipsFor(ToChoices, _settings.WaterToHour, v =>
            {
                _settings.WaterToHour = Math.Max(v, _settings.WaterFromHour + 1);
                Save();
                RenderSettings();
            })));
        }

        private void RenderTaskSettings()
        {
            var options = new WrapPanel();
            options.Children.Add(Toggle("Son des rappels", _settings.TaskSound, v => { _settings.TaskSound = v; Save(); RenderSettings(); }));
            options.Children.Add(Toggle("Masquer les terminées", _settings.HideDoneTasks, v =>
            {
                _settings.HideDoneTasks = v;
                Save();
                RenderTasks();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Options", options));

            var actions = new WrapPanel();
            actions.Children.Add(Chip("Supprimer les terminées", false, () =>
            {
                _tasks.RemoveAll(t => t.Done);
                SaveTasks();
            }));
            SettingsList.Children.Add(Row("Actions", actions));

            RenderShelfFolderSettings();
        }

        private void RenderShelfSettings()
        {
            var options = new WrapPanel();
            options.Children.Add(Toggle("S'ouvre au glisser", _settings.ShelfAutoOpen, v => { _settings.ShelfAutoOpen = v; Save(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Options", options));

            var actions = new WrapPanel();
            actions.Children.Add(Chip("Vider l'étagère", false, () =>
            {
                _shelf.Clear();
                Storage.Save("shelf", _shelf);
                RenderShelf();
            }));
            SettingsList.Children.Add(Row("Actions", actions));

            RenderShelfFolderSettings();
        }

        private void RenderSoundSettings()
        {
            SettingsList.Children.Add(Row("Pas clavier", ChipsFor(StepChoices, _settings.VolumeStepPct, v =>
            {
                _settings.VolumeStepPct = v;
                Save();
                ApplyKeyHook();
                RenderSettings();
            })));

            var options = new WrapPanel();
            options.Children.Add(Toggle("OSD Windows masqué", _settings.HideWindowsOsd, v =>
            {
                _settings.HideWindowsOsd = v;
                Save();
                ApplyKeyHook();
                RenderSettings();
            }));
            options.Children.Add(Toggle("Overlay média Windows masqué", _settings.HideWindowsMedia, v =>
            {
                _settings.HideWindowsMedia = v;
                Save();
                ApplyKeyHook();
                RenderSettings();
            }));
            options.Children.Add(Toggle("Overlays", _settings.ShowOverlays, v => { _settings.ShowOverlays = v; ApplyModules(); }));
            SettingsList.Children.Add(Row("Options", options));
        }

        private void RenderDeviceSettings()
        {
            var options = new WrapPanel();
            options.Children.Add(Toggle("Batterie dans l'en-tête", _settings.HeaderBattery, v =>
            {
                _settings.HeaderBattery = v;
                Save();
                UpdateBatteryDisplays();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Options", options));
        }

        // ───────────── Application des réglages ─────────────

        // Applique les modules sans recréer la fenêtre
        private void ApplyModules()
        {
            Save();

            TimerToggle.Visibility = (_settings.ShowTimer || _settings.ShowWater) ? Visibility.Visible : Visibility.Collapsed;   // minuteur et eau regroupés
            ShelfToggle.Visibility = _settings.ShowShelf ? Visibility.Visible : Visibility.Collapsed;
            TasksToggle.Visibility = _settings.ShowTasks ? Visibility.Visible : Visibility.Collapsed;
            WaterToggle.Visibility = Visibility.Collapsed;   // regroupé avec le minuteur
            DevicesToggle.Visibility = Visibility.Collapsed;   // regroupé dans l'onglet « Connexions »
            SearchToggle.Visibility = Visibility.Collapsed;   // la barre de recherche de l'en-tête suffit
            ClipToggle.Visibility = _settings.ShowClipboard ? Visibility.Visible : Visibility.Collapsed;
            SysToggle.Visibility = _settings.ShowSystem ? Visibility.Visible : Visibility.Collapsed;
            WeatherToggle.Visibility = (_settings.ShowWeather || _settings.ShowCalendar) ? Visibility.Visible : Visibility.Collapsed;
            NotesToggle.Visibility = _settings.ShowNotes ? Visibility.Visible : Visibility.Collapsed;
            MarketToggle.Visibility = _settings.ShowMarket ? Visibility.Visible : Visibility.Collapsed;
            ChatToggle.Visibility = _settings.ShowChat ? Visibility.Visible : Visibility.Collapsed;
            WinToggle.Visibility = _settings.ShowWindows ? Visibility.Visible : Visibility.Collapsed;
            CalToggle.Visibility = Visibility.Collapsed;   // regroupé avec la météo
            NetToggle.Visibility = Visibility.Collapsed;   // regroupé dans l'onglet « Connexions »
            SmartToggle.Visibility = _settings.ShowHome ? Visibility.Visible : Visibility.Collapsed;
            DiscordToggle.Visibility = Shared.DiscordVisible ? Visibility.Visible : Visibility.Collapsed;

            Shared.ApplyServices();
            if (!_settings.ShowMedia) OnMediaUpdated(MediaState.Empty);

            bool hidden = (_view == HoverView.Timer || _view == HoverView.Water) && !_settings.ShowTimer && !_settings.ShowWater
                       || _view == HoverView.Shelf && !_settings.ShowShelf
                       || _view == HoverView.Tasks && !_settings.ShowTasks
                       
                       || _view == HoverView.Devices && !_settings.ShowDevices
                       || _view == HoverView.Search && !_settings.ShowSearch
                       || _view == HoverView.Clipboard && !_settings.ShowClipboard
                       || _view == HoverView.System && !_settings.ShowSystem
                       || (_view == HoverView.Weather || _view == HoverView.Calendar) && !_settings.ShowWeather && !_settings.ShowCalendar
                       || _view == HoverView.Notes && !_settings.ShowNotes
                       || _view == HoverView.Market && !_settings.ShowMarket
                       || _view == HoverView.Chat && !_settings.ShowChat
                       || _view == HoverView.Discord && !Shared.DiscordVisible
                       || _view == HoverView.Smart && !_settings.ShowHome
                       || _view == HoverView.Network && !_settings.ShowNetwork
                       
                       || _view == HoverView.Windows && !_settings.ShowWindows;
            if (hidden) _view = HoverView.Auto;

            ApplyView();
            if (_hovered) UpdateState();
            RenderSettings();
        }

        private void ApplyAccent()
        {
            var brush = EffectiveAccent();
            OverlayFill.Background = brush;
            ProgressFill.Background = brush;
            _volRow?.SetAccent(brush);
            _briRow?.SetAccent(brush);
            ApplyView();   // la couleur des onglets suit l'accent
        }

        private void ShowAbout() =>
            ShowOverlay(new OverlayInfo("", 1, "Navbar 1.0"));

        // ───────────── Composants ─────────────

        private UIElement Row(string label, UIElement content)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8 * _scale) };
            // Étiquettes : colonne commune à toutes les rangées, au moins 70 et au plus 108 ; au-delà, le texte passe à la ligne au lieu de recouvrir le contrôle
            if (!Grid.GetIsSharedSizeScope(SettingsList)) Grid.SetIsSharedSizeScope(SettingsList, true);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 70 * _scale, MaxWidth = 108 * _scale, SharedSizeGroup = "SettingsLabel" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var text = new TextBlock
            {
                Text = label, Foreground = FgAlpha(0x99), TextWrapping = TextWrapping.Wrap,
                FontSize = 12 * _scale, FontFamily = UiFont,
                // Les blocs de texte longs (guides, notes) : l'étiquette reste en haut, alignée sur la première ligne
                VerticalAlignment = content is TextBlock or StackPanel ? VerticalAlignment.Top : VerticalAlignment.Center,
                Margin = new Thickness(0, content is TextBlock or StackPanel ? 1 * _scale : 0, 8 * _scale, 0),
            };
            Grid.SetColumn(content, 1);
            grid.Children.Add(text);
            grid.Children.Add(content);
            return grid;
        }

        private WrapPanel ChipsFor<T>((string Label, T Value)[] choices, T current, Action<T> pick) where T : notnull
        {
            var panel = new WrapPanel();
            foreach (var (label, value) in choices)
                panel.Children.Add(Chip(label, value.Equals(current), () => pick(value)));
            return panel;
        }

        private UIElement Toggle(string label, bool on, Action<bool> set) =>
            Chip(label, on, () => set(!on));

        private UIElement Chip(string text, bool active, Action click)
        {
            // Le chip actif prend la couleur d'accent ; le texte reste lisible sur le fond (clair ou sombre)
            var accent = EffectiveAccentColor();
            Brush bg = active ? AccentFill() : FgAlpha(0x1F);
            Brush fg = active ? new SolidColorBrush(IsLight(accent) ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White) : Fg();

            var chip = new Border
            {
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = 12 * _scale, FontFamily = UiFont },
                Background = bg,
                CornerRadius = new CornerRadius(12 * _scale), Cursor = Cursors.Hand,
                Padding = new Thickness(11 * _scale, 4 * _scale, 11 * _scale, 4 * _scale),
                Margin = new Thickness(0, 0, 5 * _scale, 4 * _scale),
            };
            chip.MouseLeftButtonUp += (_, _) => click();
            Press.Attach(chip, 0.94);
            return chip;
        }

        private static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150;

        private UIElement Swatch(string hex)
        {
            bool active = string.Equals(hex, _settings.Accent, StringComparison.OrdinalIgnoreCase);
            Brush fill;
            if (hex == "gradient") fill = new LinearGradientBrush(Color.FromRgb(0x66, 0xDB, 0xFF), Color.FromRgb(0x2D, 0x6B, 0xFF), 0);
            else
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                if (_light && color == Colors.White) color = _fg;   // même règle que l'accent réel
                fill = new SolidColorBrush(color);
            }

            var dot = new Border
            {
                Width = 20 * _scale, Height = 20 * _scale, CornerRadius = new CornerRadius(10 * _scale),
                Background = fill,
                BorderBrush = _light ? Brushes.Gray : Fg(), BorderThickness = new Thickness(active ? 2 * _scale : 0),
                Margin = new Thickness(0, 0, 8 * _scale, 4 * _scale), Cursor = Cursors.Hand,
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _settings.Accent = hex;
                Save();
                ApplyAccent();
                RenderSettings();
            };
            return dot;
        }
    }
}


































