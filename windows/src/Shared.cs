using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Navbar.Services;

namespace Navbar
{
    /// <summary>
    /// État et services partagés entre toutes les fenêtres (une par écran) : un seul lecteur média, un seul minuteur,
    /// un seul jeu de hooks clavier, et des données (tâches, étagère, eau) communes. Les fenêtres ne gardent que
    /// leur affichage, ce qui permet aussi de les recréer sans perdre un minuteur en cours.
    /// </summary>
    public static class Shared
    {
        public static Settings Settings { get; private set; } = new();

        public static SystemOverlayMonitor Monitor { get; } = new();
        public static MediaService Media { get; } = new();
        public static TimerService Timer { get; } = new();
        public static BrightnessService Brightness { get; } = new();
        public static BluetoothService Bluetooth { get; } = new();
        public static RadioService Radios { get; } = new();
        public static WifiService Wifi { get; } = new();
        public static SearchService Search { get; } = new();
        public static ClipboardHistory Clipboard { get; } = new();
        public static SystemStatsService SystemStats { get; } = new();

        public static List<string> Shelf { get; private set; } = new();
        public static List<TaskItem> Tasks { get; private set; } = new();
        public static List<WaterEntry> Water { get; private set; } = new();
        public static List<NoteItem> Notes { get; private set; } = new();

        public static MediaState LastMedia { get; private set; } = MediaState.Empty;
        public static VolumeKeyHook? KeyHook { get; private set; }

        /// <summary>Un aperçu à afficher sur tous les écrans (rappels, fin de minuteur, touches média…).</summary>
        public static event Action<OverlayInfo>? Overlay;

        /// <summary>Des données partagées ont changé ; le paramètre est la fenêtre à l'origine (déjà à jour).</summary>
        public static event Action<object?>? DataChanged;

        /// <summary>Un réglage a changé dans une fenêtre : les autres doivent se recréer pour le refléter.</summary>
        public static event Action<object?>? SettingsChanged;

        private static readonly DispatcherTimer WaterTimer = new();
        private static readonly DispatcherTimer WeatherTimer = new() { Interval = TimeSpan.FromMinutes(20) };
        private static readonly DispatcherTimer ReminderTimer = new() { Interval = TimeSpan.FromSeconds(15) };

        public static void Init(Settings settings)
        {
            Settings = settings;
            Shelf = Storage.Load("shelf", new List<string>())
                .Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)).ToList();
            Tasks = Storage.Load("tasks", new List<TaskItem>());
            Notes = Storage.Load("notes", new List<NoteItem>());
            Water = Storage.Load("water", new List<WaterEntry>()).Where(e => e.At > DateTime.Now.AddDays(-14)).ToList();

            Clipboard.Limit = settings.ClipLimit;
            Clipboard.SkipSensitive = settings.ClipSkipSensitive;
            Clipboard.Persist = settings.ClipPersist;
            Clipboard.Load();

            Media.IsAllowed = kind => !Settings.DisabledMedia.Contains(kind);
            Media.PreferredKind = settings.PreferredMedia;
            Media.Updated += m => LastMedia = m;
            Timer.Finished += label =>
            {
                if (Settings.TimerSound) System.Media.SystemSounds.Exclamation.Play();
                Overlay?.Invoke(new OverlayInfo("", 1, label + " terminé"));
            };

            Hotkey.Pressed += () => HotkeyPressed?.Invoke();
            StartMemoryTrim();
            PrewarmSystemStats();
            PrivacyTimer.Tick += (_, _) => PollPrivacy();
            WaterTimer.Tick += (_, _) => WaterReminder();
            WeatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
            WeatherTimer.Start();
            ReminderTimer.Tick += (_, _) => CheckTaskReminders();
            ReminderTimer.Start();
        }

        // ───────────── Ressources système (un seul échantillonneur pour toutes les fenêtres) ─────────────

        public static SysSnapshot? LastSystem { get; private set; }
        public static DateTime LastSystemAt { get; private set; } = DateTime.MinValue;

        /// <summary>Premier relevé en arrière-plan peu après le démarrage : compteurs de performance prêts avant d'ouvrir l'onglet.</summary>
        public static async void PrewarmSystemStats()
        {
            await System.Threading.Tasks.Task.Delay(4000);
            if (LastSystem == null) await SampleSystemAsync();
        }
        public static event Action<SysSnapshot>? SystemUpdated;

        private static readonly DispatcherTimer SysTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private static readonly HashSet<object> SysWanted = new();
        private static bool _sysBusy, _sysInit;

        /// <summary>Un module (ou la pastille) demande ou abandonne les mesures ; le relevé s'arrête quand plus personne n'en veut.</summary>
        public static void WantSystemStats(object owner, bool want)
        {
            bool added = want && SysWanted.Add(owner);
            if (!want) SysWanted.Remove(owner);

            if (!_sysInit) { SysTimer.Tick += async (_, _) => await SampleSystemAsync(); _sysInit = true; }
            bool viewing = SysWanted.Any(o => o is not string);   // une fenêtre affiche le module (la pastille seule n'a pas besoin des processus)
            SystemStats.WithTopProcesses = viewing && Settings.SysTopProcesses;
            var interval = TimeSpan.FromSeconds(viewing ? 1 : 2);
            if (SysTimer.Interval != interval) SysTimer.Interval = interval;   // réaffecter l'intervalle relance le minuteur : seulement s'il change
            if (SysWanted.Count > 0) SysTimer.Start(); else SysTimer.Stop();
            if (added && owner is not string) _ = SampleSystemAsync();   // l'onglet affiche des chiffres tout de suite, sans attendre le prochain tick
        }

        private static async System.Threading.Tasks.Task SampleSystemAsync()
        {
            if (_sysBusy) return;
            _sysBusy = true;
            try
            {
                LastSystem = await System.Threading.Tasks.Task.Run(() => SystemStats.Sample());
                LastSystemAt = DateTime.Now;
                SystemUpdated?.Invoke(LastSystem);
            }
            catch { /* compteurs indisponibles pendant un instant */ }
            finally { _sysBusy = false; }
        }

        // ───────────── Météo ─────────────

        private static readonly WeatherService WeatherSvc = new();
        public static WeatherData? Weather { get; private set; }
        public static event Action? WeatherUpdated;
        private static bool _weatherBusy;
        private static DateTime _lastRainAlert = DateTime.MinValue;

        /// <summary>Met à jour la météo (au plus toutes les 20 minutes, sauf demande explicite).</summary>
        public static async System.Threading.Tasks.Task RefreshWeatherAsync(bool force = false)
        {
            if (_weatherBusy || !Settings.ShowWeather) return;
            if (!force && Weather != null && DateTime.Now - Weather.At < TimeSpan.FromMinutes(20)) return;

            _weatherBusy = true;
            try
            {
                var data = await WeatherSvc.GetAsync(Settings.WeatherCity, Settings.WeatherFahrenheit);
                if (data != null)
                {
                    Weather = data;
                    WeatherUpdated?.Invoke();
                    if (Settings.WeatherRainAlert && !Quiet && data.RainInMin is { } rain && rain <= 45 && !data.RainingNow && DateTime.Now - _lastRainAlert > TimeSpan.FromHours(3))
                    {
                        _lastRainAlert = DateTime.Now;
                        ShowEverywhere(new OverlayInfo("\uE9BA", 1, $"Pluie dans {Math.Max(5, rain)} min", "weather"));
                    }
                }
            }
            finally { _weatherBusy = false; }
        }

        // ───────────── Bourse et crypto ─────────────

        private static readonly MarketService MarketSvc = new();
        public static MarketService Market => MarketSvc;
        public static Dictionary<string, Quote> Quotes { get; } = new();
        public static event Action? MarketUpdated;
        private static bool _marketBusy;
        private static readonly DispatcherTimer MarketTimer = new() { Interval = TimeSpan.FromMinutes(2) };
        private static bool _marketTimerInit;

        public static async System.Threading.Tasks.Task RefreshMarketAsync(bool force = false)
        {
            if (_marketBusy || !Settings.ShowMarket) return;
            if (!_marketTimerInit) { MarketTimer.Tick += async (_, _) => await RefreshMarketAsync(); MarketTimer.Start(); _marketTimerInit = true; }

            _marketBusy = true;
            try
            {
                var symbols = Settings.Watchlist.ToList();
                var fresh = symbols.Where(s => force || !Quotes.TryGetValue(s, out var q) || DateTime.Now - q.At > TimeSpan.FromMinutes(1.5)).ToList();
                var results = await System.Threading.Tasks.Task.WhenAll(fresh.Select(s => MarketSvc.GetQuoteAsync(s)));
                bool changed = false;
                foreach (var q in results)
                    if (q != null) { Quotes[q.Symbol] = q; changed = true; }
                foreach (var gone in Quotes.Keys.Where(k => !symbols.Contains(k)).ToList()) { Quotes.Remove(gone); changed = true; }
                if (changed) MarketUpdated?.Invoke();
                CheckPriceAlerts();
            }
            finally { _marketBusy = false; }
        }

        /// <summary>Déclenche une seule fois chaque alerte dont le seuil est franchi : aperçu dans la pastille, sur tous les écrans.</summary>
        private static void CheckPriceAlerts()
        {
            if (Quiet) return;
            bool any = false;
            foreach (var alert in Settings.PriceAlerts.Where(a => !a.Fired))
            {
                if (!Quotes.TryGetValue(alert.Symbol, out var q)) continue;
                if (alert.Above ? q.Price < alert.Price : q.Price > alert.Price) continue;

                alert.Fired = true;
                any = true;
                string text = $"{alert.Symbol.Split('-')[0]} {(alert.Above ? "au-dessus de" : "sous")} {MarketService.FormatPrice(alert.Price, q.Currency)}";
                ShowEverywhere(new OverlayInfo("", 1, text, "alert"));
                if (Settings.TaskSound) System.Media.SystemSounds.Asterisk.Play();
            }
            if (any) Storage.Save("settings", Settings);
        }

        public static void NotifyMarketChanged() => MarketUpdated?.Invoke();

        // ───────────── Barre des tâches Windows ─────────────

        /// <summary>Masque (ou rétablit) la barre des tâches selon le réglage ; l'état d'origine est gardé pour pouvoir le remettre.</summary>
        public static void ApplyTaskbar()
        {
            try
            {
                if (Settings.HideTaskbar)
                {
                    if (Settings.TaskbarOriginalAutoHide == null)
                    {
                        Settings.TaskbarOriginalAutoHide = TaskbarService.IsAutoHide();
                        Storage.Save("settings", Settings);   // mémorisé tout de suite : même un arrêt brutal ne perd pas l'état d'origine
                    }
                    if (!TaskbarService.IsAutoHide()) TaskbarService.SetAutoHide(true);
                }
                else RestoreTaskbar();
            }
            catch { /* barre indisponible (explorateur redémarré) : le prochain relevé réessaiera */ }
        }

        /// <summary>Remet la barre dans l'état où elle était avant l'application.</summary>
        public static void RestoreTaskbar()
        {
            if (Settings.TaskbarOriginalAutoHide is not { } original) return;
            try { TaskbarService.SetAutoHide(original); } catch { }
            Settings.TaskbarOriginalAutoHide = null;
            Storage.Save("settings", Settings);
        }

        /// <summary>À la fermeture de l'application : la barre revient (sinon plus rien ne remplace les fenêtres ouvertes).</summary>
        public static void OnExit()
        {
            if (Settings.HideTaskbar && Settings.RestoreTaskbarOnExit && Settings.TaskbarOriginalAutoHide is { } original)
            {
                try { TaskbarService.SetAutoHide(original); } catch { }
                Settings.TaskbarOriginalAutoHide = null;   // la prochaine ouverture relève de nouveau l'état d'origine
                Storage.Save("settings", Settings);
            }
        }

        // ───────────── Fenêtres ouvertes ─────────────

        public static WindowService Windows { get; } = new();
        public static PinnedAppsService Pinned { get; } = new();

        /// <summary>Clic sur un élément de la barre : lance l'application épinglée si rien n'est ouvert, sinon active ses fenêtres tour à tour (réduit la fenêtre active si elle est seule).</summary>
        public static void OpenItem(TaskbarItem item)
        {
            if (!item.Running)
            {
                LaunchNew(item);
                return;
            }
            if (item.Windows.Count == 1) { Windows.Activate(item.Windows[0].Handle); return; }

            // Plusieurs fenêtres : on passe à la suivante de l'application (la première si aucune n'est active)
            int active = item.Windows.ToList().FindIndex(w => w.Active);
            Windows.Activate(item.Windows[(active + 1) % item.Windows.Count].Handle, toggleIfActive: false);
        }

        /// <summary>Nouvelle instance de l'application (clic milieu, comme la barre des tâches).</summary>
        public static void LaunchNew(TaskbarItem item)
        {
            // Hors du fil de l'interface : le démarrage d'une application lourde (navigateur…) ne gèle plus la pastille ni ne fait croire que le clic est perdu
            string lnk = item.LnkPath, exe = item.ExePath;
            if (lnk.Length == 0 && exe.Length == 0) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lnk.Length > 0 ? lnk : exe) { UseShellExecute = true }); }
                catch when (lnk.Length > 0 && exe.Length > 0)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true }); } catch { }
                }
                catch { }
            });
        }

        // ───────────── Agenda (calendriers Home Assistant : iCloud, Google…) ─────────────

        public static List<CalEvent> CalEvents { get; private set; } = new();
        public static string CalError { get; private set; } = "";
        public static DateTime CalLoadedAt { get; private set; } = DateTime.MinValue;
        public static event Action? CalendarUpdated;
        private static readonly DispatcherTimer CalTimer = new() { Interval = TimeSpan.FromMinutes(5) };
        private static bool _calInit, _calBusy;

        /// <summary>Prochain rendez-vous (en cours ou à venir, hors journées entières).</summary>
        public static CalEvent? NextEvent => CalEvents.FirstOrDefault(e => !e.AllDay && e.End > DateTime.Now);

        public static string CalendarName(string entityId) =>
            entityId.Substring(entityId.IndexOf('.') + 1).Replace('_', ' ');

        public static async System.Threading.Tasks.Task RefreshCalendarAsync(bool force = false)
        {
            if (!_calInit) { CalTimer.Tick += async (_, _) => await RefreshCalendarAsync(); _calInit = true; }
            if (!Settings.ShowCalendar) { CalTimer.Stop(); return; }
            CalTimer.Start();
            if (_calBusy || !force && DateTime.Now - CalLoadedAt < TimeSpan.FromMinutes(4)) return;
            if (Settings.CalendarEntities.Count == 0) { CalEvents = new(); CalError = ""; CalendarUpdated?.Invoke(); return; }

            _calBusy = true;
            try
            {
                var calendars = Settings.CalendarEntities.Select(id => new CalendarInfo(id, CalendarName(id)));
                CalEvents = await HomeAssistant.EventsAsync(Settings.HaUrl, calendars, DateTime.Today, DateTime.Today.AddDays(8));
                CalError = "";
                CalLoadedAt = DateTime.Now;
            }
            catch (Exception ex) { CalError = ex.Message; }
            finally { _calBusy = false; }
            CalendarUpdated?.Invoke();
        }

        // ───────────── Réseau ─────────────

        private static readonly NetworkService NetSvc = new();
        public static NetSnapshot? LastNet { get; private set; }
        public static List<double> NetHistory => NetSvc.DownHistory;
        public static event Action? NetUpdated;
        private static readonly DispatcherTimer NetTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private static readonly HashSet<object> NetWanted = new();
        private static bool _netInit, _netBusy, _wasOnline = true;

        /// <summary>Une vue demande ou abandonne le détail (débit, pings) ; sans demande, seule la détection de coupure tourne.</summary>
        public static void WantNetwork(object owner, bool want)
        {
            bool added = want && NetWanted.Add(owner);
            if (!want) NetWanted.Remove(owner);
            ApplyNetwork();
            _ = RefreshCalendarAsync();
            if (added) _ = SampleNetworkAsync();   // mesure complète tout de suite à l'ouverture du module
        }

        private static void ApplyNetwork()
        {
            if (!_netInit) { NetTimer.Tick += async (_, _) => await SampleNetworkAsync(); _netInit = true; }
            bool run = Settings.AlertNetwork || NetWanted.Count > 0;
            NetTimer.Interval = TimeSpan.FromSeconds(NetWanted.Count > 0 ? 2 : 6);
            if (run) { if (!NetTimer.IsEnabled) { NetTimer.Start(); _ = SampleNetworkAsync(); } } else NetTimer.Stop();
        }

        private static async System.Threading.Tasks.Task SampleNetworkAsync()
        {
            if (_netBusy) return;
            _netBusy = true;
            try
            {
                LastNet = await NetSvc.SampleAsync(NetWanted.Count > 0);
                NetUpdated?.Invoke();
                if (Settings.AlertNetwork && LastNet.Online != _wasOnline)
                {
                    if (!Quiet) ShowEverywhere(new OverlayInfo(LastNet.Online ? "\uE701" : "\uF384", 1, LastNet.Online ? "Connexion rétablie" : "Connexion perdue", "net"));
                    _wasOnline = LastNet.Online;
                }
            }
            catch { /* interfaces en cours de changement */ }
            finally { _netBusy = false; }
        }

        // ───────────── Captures d'écran ─────────────

        /// <summary>Capture, enregistre en PNG, dépose dans l'étagère et le presse-papiers ; l'encoche disparaît le temps de la prise.</summary>
        public static async System.Threading.Tasks.Task TakeScreenshotAsync(ShotMode mode)
        {
            var windows = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().ToList();
            foreach (var w in windows) w.Opacity = 0;
            System.Drawing.Bitmap? bitmap = null;
            try
            {
                await System.Threading.Tasks.Task.Delay(280);
                bitmap = await ScreenshotService.CaptureAsync(mode);
            }
            catch { bitmap = null; }
            finally { foreach (var w in windows) w.Opacity = 1; }
            if (bitmap == null) return;

            using (bitmap)
            {
                string path = ScreenshotService.Save(bitmap);
                try { System.Windows.Clipboard.SetImage(ScreenshotService.ToSource(bitmap)); } catch { /* presse-papiers occupé : le fichier reste disponible */ }
                if (!Shelf.Contains(path)) Shelf.Add(path);
                Storage.Save("shelf", Shelf);
                NotifyDataChanged(null);
                ShowEverywhere(new OverlayInfo("\uE722", 1, "Capture enregistrée", "shot"));
            }
        }

        // ───────────── Notifications Windows ─────────────

        public static NotificationWatcher Notifications { get; } = new();
        private static bool _notifInit;

        private static async void ApplyNotifications()
        {
            if (!_notifInit)
            {
                Notifications.Received += (app, text) =>
                {
                    if (Quiet) return;
                    string label = text.Length > 0 ? $"{app} · {text}" : app;
                    if (label.Length > 42) label = label.Substring(0, 41) + "…";
                    ShowEverywhere(new OverlayInfo("\uE715", 1, label, "notif"));
                };
                _notifInit = true;
            }
            Notifications.HideContent = Settings.NotificationsHideContent;
            if (Settings.ShowNotifications) await Notifications.StartAsync(); else Notifications.Stop();
        }

        // ───────────── Discord (vocal) ─────────────

        public static DiscordService Discord { get; } = new();
        public static event Action? DiscordChanged;
        private static readonly DispatcherTimer DiscordTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private static bool _discordInit, _discordBusy;

        /// <summary>L'application Discord est lancée : l'onglet apparaît tout seul et la connexion se fait sans action.</summary>
        public static bool DiscordRunning { get; private set; }
        public static bool DiscordVisible => Settings.ShowDiscord && DiscordRunning;

        /// <summary>Titre de la fenêtre Discord (« # général | Serveur - Discord », « (3) … » s'il y a des mentions) ; vide si réduit dans la zone de notification.</summary>
        public static string DiscordTitle { get; private set; } = "";

        private static bool DetectDiscord()
        {
            bool found = false;
            string title = "";
            foreach (var name in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    found = true;
                    try { if (p.MainWindowTitle.Length > title.Length) title = p.MainWindowTitle; } catch { }
                    p.Dispose();
                }
            // Appelé hors du thread d'interface : la notification y repasse
            if (title != DiscordTitle)
            {
                DiscordTitle = title;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => DiscordChanged?.Invoke());
            }
            return found;
        }

        public static async System.Threading.Tasks.Task ConnectDiscordAsync(bool force = false)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } dlog)   // diagnostic : présence des réglages, jamais leurs valeurs
                System.IO.File.AppendAllText(dlog, $"connect-try busy={_discordBusy} show={Settings.ShowDiscord} clientIdLen={Settings.DiscordClientId.Trim().Length} hasSecret={DiscordService.HasSecret} connected={Discord.Connected} running={DiscordRunning}\n");
#endif
            if (_discordBusy) { if (!force) return; }   // un clic explicite relance même si une tentative automatique traîne
            _discordBusy = true;
            try
            {
                bool running = await System.Threading.Tasks.Task.Run(DetectDiscord);
                if (running != DiscordRunning) { DiscordRunning = running; DiscordChanged?.Invoke(); }
                if (running) await Discord.EnsureConnectedAsync(Settings.DiscordClientId.Trim());
                else if (Discord.Connected) Discord.Disconnect();
            }
            finally { _discordBusy = false; }
        }

        private static void ApplyDiscord()
        {
            if (!_discordInit)
            {
                Discord.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
#if DEBUG
                    if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } log)   // état seulement, jamais de noms
                        System.IO.File.AppendAllText(log, $"discord status=\"{Discord.Status}\" connected={Discord.Connected} inChannel={Discord.InChannel} members={Discord.Members.Count} muted={Discord.Muted} deaf={Discord.Deafened} running={DiscordRunning} visible={DiscordVisible}\n");
#endif
                    DiscordChanged?.Invoke();
                });
                DiscordTimer.Tick += async (_, _) => await ConnectDiscordAsync();   // Discord peut être lancé après l'application
                _discordInit = true;
            }
            if (Settings.ShowDiscord) { DiscordTimer.Start(); _ = ConnectDiscordAsync(); }
            else { DiscordTimer.Stop(); Discord.Disconnect(); DiscordRunning = false; }
        }

        // ───────────── Confidentialité : micro, caméra, capture d'écran ─────────────

        private static readonly PrivacyMonitor PrivacyMon = new();
        private static readonly DispatcherTimer PrivacyTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        public static PrivacyState Privacy { get; private set; } = PrivacyState.None;
        public static event Action? PrivacyChanged;

        private static void PollPrivacy()
        {
            var now = PrivacyMon.Read();
            bool inCallNow = now.Microphone || now.Camera;
            if (inCallNow && MeetingSince == null) { MeetingSince = DateTime.Now; PrivacyChanged?.Invoke(); }
            else if (!inCallNow && MeetingSince != null) { MeetingSince = null; PrivacyChanged?.Invoke(); }
            if (now.Microphone == Privacy.Microphone && now.Camera == Privacy.Camera && now.Screen == Privacy.Screen) return;

            var before = Privacy;
            Privacy = now;
            PrivacyChanged?.Invoke();

            // Un aperçu à l'instant où une ressource est ouverte, avec l'application concernée
            string who = now.Apps.Count > 0 ? " · " + string.Join(", ", now.Apps.Take(2)) : "";
            if (now.Camera && !before.Camera) ShowEverywhere(new OverlayInfo("\uE714", 1, "Caméra utilisée" + who, "privacy"));
            else if (now.Microphone && !before.Microphone) ShowEverywhere(new OverlayInfo("\uE720", 1, "Micro utilisé" + who, "privacy"));
            else if (now.Screen && !before.Screen) ShowEverywhere(new OverlayInfo("\uE7F4", 1, "Capture d'écran en cours" + who, "privacy"));
        }

        // ───────────── Raccourci global ─────────────

        public static HotkeyService Hotkey { get; } = new();
        public static event Action? HotkeyPressed;

        // ───────────── Profils et contexte ─────────────

        /// <summary>Réglages personnels qu'un profil ne touche pas (identifiants, listes de suivi, alertes).</summary>
        public static readonly string[] ProfileExcluded = { "DiscordClientId", "ChatCustomUrl", "Watchlist", "PriceAlerts", "ChatModels", "ContextAuto", "ContextProfiles", "Routines", "HaEntities", "HaUrl", "DirigeraIp", "CalendarEntities", "ShelfFolders" };
        public static event Action? RebuildAllRequested;

        public static string SnapshotJson()
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(Settings))!.AsObject();
            foreach (var key in ProfileExcluded) node.Remove(key);
            return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Applique les réglages d'un profil (seules les propriétés présentes sont reprises) puis sauvegarde.</summary>
        public static void ApplyProfileJson(string json, bool rebuild = true)
        {
            var incoming = System.Text.Json.JsonSerializer.Deserialize<Settings>(json) ?? throw new System.IO.InvalidDataException("Profil vide");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var prop in typeof(Settings).GetProperties())
            {
                if (ProfileExcluded.Contains(prop.Name) || !prop.CanWrite || !doc.RootElement.TryGetProperty(prop.Name, out _)) continue;
                prop.SetValue(Settings, prop.GetValue(incoming));
            }
            Storage.Save("settings", Settings);
            ApplyServices();
            if (rebuild) RebuildAllRequested?.Invoke();
        }

        public static string Context { get; private set; } = "normal";
        private static string _contextCandidate = "normal";
        private static string? _beforeAuto;
        private static readonly DispatcherTimer ContextTimer = new() { Interval = TimeSpan.FromSeconds(6) };
        private static bool _contextInit;

        private static void ApplyContext()
        {
            if (!_contextInit)
            {
                ContextTimer.Tick += (_, _) => PollContext();
                _contextInit = true;
            }
            if (Settings.ContextAuto) ContextTimer.Start(); else ContextTimer.Stop();
        }

        private static void PollContext()
        {
            string now = InMeeting ? "reunion" : FullscreenDetector.IsActive() ? "jeu" : "normal";
            // Deux relevés identiques d'affilée : un simple changement de fenêtre ne bascule pas tout
            if (now != _contextCandidate) { _contextCandidate = now; return; }
            if (now == Context) return;

            string previous = Context;
            Context = now;
            var profiles = Storage.Load("profiles", new Dictionary<string, string>());
            string? target = Settings.ContextProfiles.TryGetValue(now, out var name) && profiles.TryGetValue(name, out var json) ? json : null;

            if (target != null)
            {
                if (previous == "normal") _beforeAuto = SnapshotJson();   // on mémorise l'état d'avant pour le rétablir au retour
                ApplyProfileJson(target);
            }
            else if (now == "normal" && _beforeAuto != null)
            {
                ApplyProfileJson(_beforeAuto);
                _beforeAuto = null;
            }
        }

        // ───────────── Mémoire : on rend les pages inutilisées à Windows quand personne ne regarde la pastille ─────────────

        public static int HoverCount;
        private static readonly DispatcherTimer TrimTimer = new() { Interval = TimeSpan.FromSeconds(45) };

        [System.Runtime.InteropServices.DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);

        public static void StartMemoryTrim()
        {
            TrimTimer.Tick += (_, _) =>
            {
                if (HoverCount > 0) return;
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
                try { EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }
            };
            TrimTimer.Start();
        }

        public static void ShowEverywhere(OverlayInfo info) => Overlay?.Invoke(info);
        public static void NotifyDataChanged(object? sender) => DataChanged?.Invoke(sender);
        public static void NotifySettingsChanged(object? sender) => SettingsChanged?.Invoke(sender);

        /// <summary>Démarre ou arrête les services selon les réglages (surveillance système, média, hooks, rappel d'eau).</summary>
        public static void ApplyServices()
        {
            if (Settings.ShowOverlays) Monitor.Start(); else Monitor.Stop();
            Media.PreferredKind = Settings.PreferredMedia;
            if (Settings.ShowMedia) Media.Start(); else Media.Stop();

            bool hook = Settings.ShowOverlays && (Settings.HideWindowsOsd || Settings.HideWindowsMedia);
            if (hook)
            {
                KeyHook ??= new VolumeKeyHook(Monitor.Volume) { MediaKey = OnMediaKey };
                KeyHook.StepSize = Settings.VolumeStepPct / 100f;
                KeyHook.HandleVolumeKeys = Settings.HideWindowsOsd;
                KeyHook.HandleMediaKeys = Settings.HideWindowsMedia;
                KeyHook.Start();
            }
            else KeyHook?.Stop();

            SetWaterInterval(Settings.WaterMinutes);

            WantSystemStats("pill", Settings.PillSystem);

            if (Settings.PrivacyDots || Settings.MeetingMode) PrivacyTimer.Start();
            else { PrivacyTimer.Stop(); if (Privacy.Any) { Privacy = PrivacyState.None; PrivacyChanged?.Invoke(); } }

            if (Settings.HotkeyEnabled) Hotkey.Start(Settings.HotkeyChoice); else Hotkey.Stop();
            _ = RefreshWeatherAsync();
            _ = RefreshMarketAsync();
            ApplyDiscord();
            ApplyContext();
            ApplyTaskbar();
            ApplyNetwork();
            _ = RefreshCalendarAsync();
            ApplyNotifications();

            Clipboard.Limit = Settings.ClipLimit;
            Clipboard.SkipSensitive = Settings.ClipSkipSensitive;
            Clipboard.Persist = Settings.ClipPersist;
            Clipboard.Trim();
            if (Settings.ShowClipboard) Clipboard.Start(); else Clipboard.Stop();
        }

        // ───────────── Touches média ─────────────

        /// <summary>Touches média interceptées : on agit une seule fois sur le lecteur et tous les écrans affichent l'aperçu.</summary>
        private static async void OnMediaKey(int vk)
        {
            var before = LastMedia;
            if (!before.HasMedia) return;

            switch (vk)
            {
                case 0xB3:
                    Media.TogglePlayPause();
                    ShowEverywhere(new OverlayInfo(before.Playing ? "" : "", 1, before.Title, "media"));
                    break;
                case 0xB0:
                    Media.Next();
                    ShowEverywhere(new OverlayInfo("", 1, "Piste suivante", "media"));
                    await System.Threading.Tasks.Task.Delay(900);
                    ShowEverywhere(new OverlayInfo("", 1, LastMedia.Title, "media"));
                    break;
                case 0xB1:
                    Media.Previous();
                    ShowEverywhere(new OverlayInfo("", 1, "Piste précédente", "media"));
                    await System.Threading.Tasks.Task.Delay(900);
                    ShowEverywhere(new OverlayInfo("", 1, LastMedia.Title, "media"));
                    break;
            }
        }

        // ───────────── Eau ─────────────

        public static int TodayMl => Water.Where(e => e.At.Date == DateTime.Today).Sum(e => e.Ml);

        /// <summary>Rappel d'eau périodique ; 0 désactive.</summary>
        public static void SetWaterInterval(int minutes)
        {
            WaterTimer.Stop();
            if (minutes <= 0) return;
            WaterTimer.Interval = TimeSpan.FromMinutes(minutes);
            WaterTimer.Start();
        }

        /// <summary>Seulement dans la plage d'heures choisie et tant que l'objectif n'est pas atteint.</summary>
        /// <summary>Mode concentration : manuel, ou automatique pendant une session Pomodoro « Focus » ; les rappels attendent la fin.</summary>
        /// <summary>Début de la réunion en cours (micro ou caméra utilisés), sinon null.</summary>
        public static DateTime? MeetingSince { get; private set; }
        public static bool InMeeting => Settings.MeetingMode && MeetingSince != null;

        public static bool Quiet => Settings.QuietManual || InMeeting || Settings.QuietWithFocus && Timer.Status == TimerStatus.Running && Timer.Label == "Focus";

        private static void WaterReminder()
        {
            if (Quiet) return;
            int hour = DateTime.Now.Hour;
            if (hour < Settings.WaterFromHour || hour >= Settings.WaterToHour) return;

            int total = TodayMl, goal = Math.Max(1, Settings.WaterGoalMl);
            if (total >= goal) return;

            ShowEverywhere(new OverlayInfo("\U0001F4A7", total / (double)goal,
                $"Bois un verre · {(total / 1000.0):0.##}/{(goal / 1000.0):0.##} L"));
        }

        // ───────────── Rappels de tâches ─────────────

        private static readonly HashSet<string> RemindedEvents = new();

        /// <summary>Aperçu quelques minutes avant un rendez-vous (une seule fois par événement).</summary>
        private static void CheckCalendarReminders()
        {
            int lead = Settings.CalendarReminderMin;
            if (lead <= 0 || Quiet) return;
            foreach (var ev in CalEvents.Where(e => !e.AllDay))
            {
                var delta = ev.Start - DateTime.Now;
                if (delta <= TimeSpan.Zero || delta > TimeSpan.FromMinutes(lead)) continue;
                if (!RemindedEvents.Add(ev.Title + "|" + ev.Start.Ticks)) continue;

                string text = $"{(ev.Title.Length > 26 ? ev.Title.Substring(0, 25) + "…" : ev.Title)} dans {Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes))} min";
                ShowEverywhere(new OverlayInfo("\uE787", 1, text, "calendar"));
                if (Settings.TaskSound) System.Media.SystemSounds.Asterisk.Play();
            }
        }

        private static void CheckTaskReminders()
        {
            CheckCalendarReminders();
            if (Quiet) return;
            var due = Tasks.FirstOrDefault(t => !t.Done && !t.Notified && t.DueAt != null && t.DueAt <= DateTime.Now);
            if (due == null) return;

            due.Notified = true;
            Storage.Save("tasks", Tasks);
            NotifyDataChanged(null);
            if (Settings.TaskSound) System.Media.SystemSounds.Asterisk.Play();
            ShowEverywhere(new OverlayInfo("", 1, due.Text));
        }
    }
}
































