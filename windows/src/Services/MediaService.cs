using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Navbar.Services
{
    public sealed record MediaState(bool HasMedia, string Title, string Artist, bool Playing,
                                    double Position, double Duration, BitmapSource? Art,
                                    string Source = "", int SourceIndex = 0, int SourceCount = 0, bool CanSeek = true)
    {
        public static readonly MediaState Empty = new(false, "", "", false, 0, 0, null);
    }

    /// <summary>
    /// Lecture en cours via les contrôles média de Windows : VLC, Lecteur multimédia, Apple Music, Spotify,
    /// Chrome, Edge, Firefox (dont YouTube). Plusieurs sources peuvent coexister : on affiche celle qui joue,
    /// et on peut passer de l'une à l'autre. VLC et l'ancien Lecteur Windows Media, qui ne se déclarent pas
    /// toujours à Windows, sont repérés par le titre de leur fenêtre et pilotés par les touches média.
    /// </summary>
    public sealed record MediaSourceInfo(string Id, string Label, bool Playing, bool Active);

    public sealed class MediaService
    {
        public event Action<MediaState>? Updated;

        /// <summary>Sources de lecture présentes (Spotify, Chrome, VLC…), la courante marquée active.</summary>
        public IReadOnlyList<MediaSourceInfo> Sources { get; private set; } = Array.Empty<MediaSourceInfo>();

        /// <summary>Famille préférée (spotify, chrome…) : choisie en premier quand plusieurs sources jouent ; vide = automatique.</summary>
        public string PreferredKind { get; set; } = "";

        /// <summary>Filtre des sources autorisées (vlc, wmp, apple, spotify, chrome, edge, firefox, youtube, other).</summary>
        public Func<string, bool> IsAllowed { get; set; } = _ => true;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000) };

        /// <summary>Vrai quand la pastille est survolée : relevé deux fois par seconde (barre de progression) ; sinon une fois par seconde.</summary>
        public bool FastPolling
        {
            get => _timer.Interval < TimeSpan.FromMilliseconds(800);
            set
            {
                var interval = TimeSpan.FromMilliseconds(value ? 500 : 1000);
                if (_timer.Interval != interval) _timer.Interval = interval;   // réaffecter relance le minuteur : seulement s'il change
            }
        }
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private bool _busy;

        // Source choisie à la main (identifiant) ; vide = choix automatique (celle qui joue)
        private string? _pinned;
        private string _activeId = "";
        private List<string> _ids = new();

        private string _title = "", _artist = "";
        private BitmapSource? _art;
        private string _artHash = "";
        private DateTime _changedAt = DateTime.MinValue;
        private DateTime _lastArtCheck = DateTime.MinValue;

        private double _knownDuration, _basePos;
        private DateTime _baseAt = DateTime.UtcNow;

        public MediaService()
        {
            _timer.Tick += async (_, _) => await RefreshAsync();
        }

        public async void Start()
        {
            try { _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
            catch { return; }
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        // ───────────── Commandes ─────────────

        public async void TogglePlayPause() => await RunAsync(s => s.TryTogglePlayPauseAsync(), 0xB3);
        public async void Next() => await RunAsync(s => s.TrySkipNextAsync(), 0xB0);
        public async void Previous() => await RunAsync(s => s.TrySkipPreviousAsync(), 0xB1);

        public async void Seek(double seconds)
        {
            _basePos = seconds; _baseAt = DateTime.UtcNow;
            await RunAsync(s => s.TryChangePlaybackPositionAsync((long)(seconds * 10_000_000)), null);
        }

        /// <summary>Passe à la source suivante (Spotify → Chrome → VLC…) et la fixe jusqu'à ce qu'elle disparaisse.</summary>
        public void NextSource()
        {
            if (_ids.Count < 2) return;
            int index = _ids.IndexOf(_activeId);
            _pinned = _ids[(index + 1) % _ids.Count];
            ResetTrack();
            _ = RefreshAsync();
        }

        /// <summary>Fixe directement une source (clic sur sa pastille).</summary>
        public void SelectSource(string id)
        {
            _pinned = id;
            ResetTrack();
            _ = RefreshAsync();
        }

        /// <summary>Revient au choix automatique (la source qui joue).</summary>
        public void AutoSource() { _pinned = null; ResetTrack(); }

        private async Task RunAsync(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command, int? mediaKey)
        {
            try
            {
                var session = FindSession(_activeId);
                if (session != null) await command(session);
                else if (mediaKey != null) PressMediaKey(mediaKey.Value);   // VLC et co. sans session Windows
                else return;

                await Task.Delay(250);   // laisse le lecteur mettre à jour son état avant de relire
                await RefreshAsync();
            }
            catch { }
        }

        private GlobalSystemMediaTransportControlsSession? FindSession(string id) =>
            _manager?.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == id);

        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, int flags, int extra);

        private static void PressMediaKey(int vk)
        {
            keybd_event((byte)vk, 0, 0, 0);
            keybd_event((byte)vk, 0, 2, 0);
        }

        private void ResetTrack()
        {
            _title = _artist = "";
            _art = null; _artHash = "";
            _knownDuration = 0; _basePos = 0;
            _changedAt = DateTime.UtcNow;
            _lastArtCheck = DateTime.MinValue;
        }

        // ───────────── Reconnaissance des sources ─────────────

        private sealed record Candidate(string Id, string Kind, string Label, string Process, bool Playing,
                                     GlobalSystemMediaTransportControlsSession? Session, string WindowTitle = "");

        /// <summary>Famille de lecteur d'après l'identifiant d'application Windows.</summary>
        public static string KindOf(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("spotify")) return "spotify";
            if (s.Contains("applemusic") || s.Contains("apple music") || s.Contains("itunes") || s.Contains("appleinc")) return "apple";
            if (s.Contains("vlc")) return "vlc";
            if (s.Contains("zunemusic") || s.Contains("zunevideo") || s.Contains("wmplayer") || s.Contains("mediaplayer") || s.Contains("windows media")) return "wmp";
            if (s.Contains("chrome")) return "chrome";
            if (s.Contains("msedge") || s.Contains("edge")) return "edge";
            if (s.Contains("firefox") || s.Contains("308046b0af4a39cb") || s.Contains("mozilla")) return "firefox";
            return "other";
        }

        public static string LabelOf(string kind, string id) => kind switch
        {
            "spotify" => "Spotify",
            "apple" => "Apple Music",
            "vlc" => "VLC",
            "wmp" => "Lecteur multimédia",
            "chrome" => "Chrome",
            "edge" => "Edge",
            "firefox" => "Firefox",
            _ => FriendlyName(id),
        };

        private static string FriendlyName(string id)
        {
            string name = id.Split('!')[0].Split('_')[0];
            name = name.Contains('.') ? name.Substring(name.LastIndexOf('.') + 1) : name;
            name = Path.GetFileNameWithoutExtension(name);
            return name.Length > 0 ? char.ToUpper(name[0]) + name.Substring(1) : "Média";
        }

        // Navigateurs connus : processus, nom affiché, famille (réglage « Chrome », « Edge » ou « Firefox »)
        private static readonly (string Process, string Label, string Kind)[] Browsers =
        {
            ("chrome", "Chrome", "chrome"), ("msedge", "Edge", "edge"), ("firefox", "Firefox", "firefox"),
            ("zen", "Zen", "firefox"), ("librewolf", "LibreWolf", "firefox"), ("waterfox", "Waterfox", "firefox"), ("floorp", "Floorp", "firefox"),
            ("brave", "Brave", "chrome"), ("opera", "Opera", "chrome"), ("vivaldi", "Vivaldi", "chrome"), ("arc", "Arc", "chrome"),
        };

        // Les navigateurs de la famille Firefox se déclarent à Windows par un identifiant haché propre à l'installation
        private static bool IsHashedId(string id) =>
            id.Length is >= 10 and <= 20 && id.All(Uri.IsHexDigit);

        private readonly Dictionary<string, (DateTime At, (string Process, string Label, string Kind)? Browser)> _hashedCache = new();

        /// <summary>Retrouve quel navigateur se cache derrière un identifiant haché, d'après les processus lancés.</summary>
        private (string Process, string Label, string Kind)? ResolveHashed(string id)
        {
            if (_hashedCache.TryGetValue(id, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(15)) return cached.Browser;

            (string, string, string)? found = null;
            // On privilégie la famille Firefox (c'est elle qui utilise ces identifiants hachés)
            foreach (var b in Browsers.OrderBy(b => b.Kind == "firefox" ? 0 : 1))
            {
                try
                {
                    var running = Process.GetProcessesByName(b.Process);
                    bool any = running.Length > 0;
                    foreach (var p in running) p.Dispose();
                    if (any) { found = b; break; }
                }
                catch { }
            }
            _hashedCache[id] = (DateTime.UtcNow, found);
            return found;
        }

        /// <summary>YouTube dans le navigateur : repéré par le titre d'une de ses fenêtres (« … - YouTube »).</summary>
        private static readonly Dictionary<string, (DateTime At, bool Result)> YouTubeCache = new();

        private static bool BrowserShowsYouTube(string processName)
        {
            // Énumérer processus et fenêtres coûte cher : le résultat est gardé 3 secondes
            if (YouTubeCache.TryGetValue(processName, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(3)) return cached.Result;
            bool result = BrowserShowsYouTubeUncached(processName);
            YouTubeCache[processName] = (DateTime.UtcNow, result);
            return result;
        }

        private static bool BrowserShowsYouTubeUncached(string processName)
        {
            try
            {
                var pids = Process.GetProcessesByName(processName).Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
                if (pids.Count == 0) return false;

                bool found = false;
                EnumWindows((h, _) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    GetWindowThreadProcessId(h, out uint pid);
                    if (!pids.Contains(pid)) return true;

                    var title = new System.Text.StringBuilder(256);
                    GetWindowText(h, title, title.Capacity);
                    if (title.ToString().Contains("YouTube", StringComparison.OrdinalIgnoreCase)) { found = true; return false; }
                    return true;
                }, IntPtr.Zero);
                return found;
            }
            catch { return false; }
        }

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

        /// <summary>Fenêtre de VLC ou de l'ancien Lecteur Windows Media quand ils n'ont pas de session Windows.</summary>
        private static DateTime _fallbackAt = DateTime.MinValue;
        private static List<Candidate> _fallbackCache = new();

        private static IEnumerable<Candidate> WindowFallbacks(IEnumerable<string> knownKinds)
        {
            // Recherche de VLC / Lecteur Windows Media par processus : gardée 3 secondes
            if (DateTime.UtcNow - _fallbackAt > TimeSpan.FromSeconds(3))
            {
                _fallbackCache = WindowFallbacksUncached(new string[0]).ToList();
                _fallbackAt = DateTime.UtcNow;
            }
            var haveKinds = knownKinds.ToHashSet();
            return _fallbackCache.Where(c => !haveKinds.Contains(c.Kind));
        }

        private static IEnumerable<Candidate> WindowFallbacksUncached(IEnumerable<string> knownKinds)
        {
            var known = knownKinds.ToHashSet();
            foreach (var (kind, process, suffix) in new[]
                     { ("vlc", "vlc", " - VLC media player"), ("wmp", "wmplayer", " - Lecteur Windows Media") })
            {
                if (known.Contains(kind)) continue;
                Process[] processes;
                try { processes = Process.GetProcessesByName(process); } catch { continue; }

                foreach (var p in processes)
                {
                    using (p)
                    {
                        string title = p.MainWindowTitle ?? "";
                        if (title.Length == 0) continue;
                        bool idle = title.Equals("VLC media player", StringComparison.OrdinalIgnoreCase)
                                    || title.StartsWith("Lecteur Windows Media", StringComparison.OrdinalIgnoreCase);
                        if (idle) continue;   // lecteur ouvert mais sans média
                        if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) title = title.Substring(0, title.Length - suffix.Length);
                        yield return new Candidate("window:" + kind, kind, LabelOf(kind, kind), process, true, null, title);
                        break;
                    }
                }
            }
        }

        // ───────────── Relevé ─────────────

        private async Task RefreshAsync()
        {
            if (_busy || _manager == null) return;
            _busy = true;
            try
            {
                var current = _manager.GetCurrentSession();
                var candidates = new List<Candidate>();
                foreach (var s in _manager.GetSessions())
                {
                    string id = s.SourceAppUserModelId;
                    string kind = KindOf(id), label = LabelOf(kind, id), process = "";
                    if (kind == "other" && IsHashedId(id) && ResolveHashed(id) is { } browser)
                        (process, label, kind) = browser;
                    else if (kind is "chrome" or "edge" or "firefox")
                    {
                        // Processus du navigateur : celui dont le nom figure dans l'identifiant, sinon le principal de la famille
                        var match = Browsers.FirstOrDefault(b => b.Kind == kind && id.Contains(b.Process, StringComparison.OrdinalIgnoreCase));
                        process = match.Process ?? Browsers.First(b => b.Kind == kind).Process;
                    }
                    if (!IsAllowed(kind)) continue;
                    bool playing = s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    candidates.Add(new Candidate(id, kind, label, process, playing, s));
                }
                candidates.AddRange(WindowFallbacks(candidates.Select(c => c.Kind)).Where(c => IsAllowed(c.Kind)));
                candidates = candidates.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList();
                _ids = candidates.Select(c => c.Id).ToList();

                if (candidates.Count == 0) { Updated?.Invoke(MediaState.Empty); return; }

                // Choix : la source épinglée si elle existe encore ; sinon celle qui joue (la « courante » de Windows d'abord)
                var chosen = candidates.FirstOrDefault(c => c.Id == _pinned)
                          ?? (PreferredKind.Length > 0 ? candidates.FirstOrDefault(c => c.Playing && c.Kind == PreferredKind) : null)
                          ?? candidates.FirstOrDefault(c => c.Playing && c.Session != null && current != null && c.Id == current.SourceAppUserModelId)
                          ?? candidates.FirstOrDefault(c => c.Playing)
                          ?? candidates.FirstOrDefault(c => current != null && c.Id == current.SourceAppUserModelId)
                          ?? candidates[0];

                if (chosen.Id != _activeId) { _activeId = chosen.Id; ResetTrack(); }
                Sources = candidates.Select(c => new MediaSourceInfo(c.Id, c.Label, c.Playing, c.Id == chosen.Id)).ToList();

                bool youtube = chosen.Process.Length > 0 && BrowserShowsYouTube(chosen.Process);
                if (youtube && !IsAllowed("youtube")) { Updated?.Invoke(MediaState.Empty); return; }

                string source = chosen.Label + (youtube ? " · YouTube" : "");
                int index = candidates.IndexOf(chosen) + 1, count = candidates.Count;

                if (chosen.Session == null)
                {
                    PublishWindowFallback(chosen, source, index, count);
                    return;
                }

                var session = chosen.Session;
                var props = await session.TryGetMediaPropertiesAsync();
                string title = props.Title ?? "", artist = props.Artist ?? "";
                if (title.Length == 0 && artist.Length == 0) { Updated?.Invoke(MediaState.Empty); return; }

                if (title != _title || artist != _artist)
                {
                    _title = title; _artist = artist;
                    _art = null; _artHash = "";
                    _knownDuration = 0; _basePos = 0;
                    _changedAt = DateTime.UtcNow;
                    _lastArtCheck = DateTime.MinValue;
                }

                // Les lecteurs web changent le titre avant la vignette : on la relit régulièrement
                // et on ne remplace l'image que si son contenu a réellement changé.
                var now = DateTime.UtcNow;
                var every = _art == null ? TimeSpan.FromMilliseconds(500)
                          : now - _changedAt < TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(1.5)
                          : TimeSpan.FromSeconds(5);
                if (props.Thumbnail != null && now - _lastArtCheck > every)
                {
                    _lastArtCheck = now;
                    var (hash, img) = await LoadArtAsync(props, _artHash);
                    if (img != null) { _art = img; _artHash = hash; }
                }

                bool playingNow = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var tl = session.GetTimelineProperties();
                double duration = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds);
                double pos;
                if (duration > 0)
                {
                    pos = tl.Position.TotalSeconds;
                    if (playingNow) pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
                    _knownDuration = duration; _basePos = pos; _baseAt = DateTime.UtcNow;
                }
                else if (_knownDuration > 0)
                {
                    // Certains sites effacent leur position après un déplacement : on extrapole depuis la dernière valeur connue
                    duration = _knownDuration;
                    pos = _basePos + (playingNow ? (DateTime.UtcNow - _baseAt).TotalSeconds : 0);
                    if (!playingNow) { _basePos = pos; }
                    _baseAt = DateTime.UtcNow;
                }
                else pos = 0;
                pos = Math.Clamp(pos, 0, duration > 0 ? duration : pos);

                Updated?.Invoke(new MediaState(true, _title, _artist, playingNow, pos, duration, _art, source, index, count));
            }
            catch { /* session disparue pendant la lecture : on réessaie au tick suivant */ }
            finally { _busy = false; }
        }

        /// <summary>Lecteur sans session Windows : titre de la fenêtre, pas de pochette ni de position.</summary>
        private void PublishWindowFallback(Candidate chosen, string source, int index, int count)
        {
            if (chosen.WindowTitle != _title) { _title = chosen.WindowTitle; _artist = ""; _art = null; _artHash = ""; }
            Updated?.Invoke(new MediaState(true, chosen.WindowTitle, chosen.Label, true, 0, 0, null, source, index, count, CanSeek: false));
        }

        private static async Task<(string Hash, BitmapSource? Image)> LoadArtAsync(
            GlobalSystemMediaTransportControlsSessionMediaProperties props, string knownHash)
        {
            try
            {
                using var ras = await props.Thumbnail.OpenReadAsync();
                using Stream stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(ras);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);

                string hash = Convert.ToHexString(SHA1.HashData(buffer.ToArray()));
                if (hash == knownHash) return (hash, null);

                buffer.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160;
                bmp.StreamSource = buffer;
                bmp.EndInit();
                bmp.Freeze();
                return (hash, bmp);
            }
            catch { return (knownHash, null); }
        }
    }
}








