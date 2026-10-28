using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Navbar.Services
{
    public sealed record PinnedApp(string Name, string LnkPath, string Key, ImageSource? Icon, string Target = "", bool Extra = false);

    /// <summary>Un élément de la barre : une application épinglée (ouverte ou non) ou une fenêtre ouverte non épinglée.</summary>
    public sealed record TaskbarItem(string Name, ImageSource? Icon, bool Pinned, string LnkPath, IReadOnlyList<AppWindow> Windows, string ExePath = "", bool Extra = false, string PinKey = "")
    {
        /// <summary>Clé stable pour mémoriser l'ordre choisi par l'utilisateur.</summary>
        public string OrderKey => Pinned ? "p:" + PinKey : "w:" + System.IO.Path.GetFileNameWithoutExtension(ExePath);
        public bool Running => Windows.Count > 0;
        public bool Active => Windows.Any(w => w.Active);
        public bool Minimized => Windows.Count > 0 && Windows.All(w => w.Minimized);
        public string Title => Windows.Count == 0 ? Name : Windows.Count == 1 ? Windows[0].Title : $"{Name} · {Windows.Count} fenêtres";
    }

    /// <summary>
    /// Applications épinglées à la barre des tâches Windows : lues dans le dossier des raccourcis épinglés de l'utilisateur
    /// (%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar), dans l'ordre d'épinglage. Lecture seule.
    /// </summary>
    public sealed class PinnedAppsService
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

        public event Action? Changed;
        public IReadOnlyList<PinnedApp> Apps { get; private set; } = Array.Empty<PinnedApp>();

        private FileSystemWatcher? _watcher;
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
        private readonly Dictionary<string, ImageSource?> _icons = new();
        private int _consumers;

        public PinnedAppsService() => _debounce.Tick += (_, _) => { _debounce.Stop(); Load(); };

        public void Acquire()
        {
            if (_consumers++ > 0) return;
            Load();
            try
            {
                if (!Directory.Exists(Folder)) return;
                _watcher = new FileSystemWatcher(Folder) { EnableRaisingEvents = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                FileSystemEventHandler any = (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
                _watcher.Created += any; _watcher.Deleted += any; _watcher.Changed += any;
                _watcher.Renamed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
            }
            catch { /* pas de surveillance : la liste se relit à la prochaine ouverture */ }
        }

        public void Release()
        {
            if (_consumers == 0 || --_consumers > 0) return;
            _watcher?.Dispose();
            _watcher = null;
        }

        public void Load()
        {
            var apps = new List<PinnedApp>();
            try
            {
                if (Directory.Exists(Folder))
                {
                    var shell = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic? wsh = shell == null ? null : Activator.CreateInstance(shell);
                    foreach (var file in new DirectoryInfo(Folder).GetFiles("*.lnk").OrderBy(f => f.CreationTime))
                    {
                        string name = Path.GetFileNameWithoutExtension(file.Name);
                        string target = "", args = "", iconLocation = "";
                        try { if (wsh != null) { var link = wsh.CreateShortcut(file.FullName); target = (string)link.TargetPath; args = (string)link.Arguments; iconLocation = (string)link.IconLocation; } } catch { }
                        if (Shared.Settings.HiddenPinned.Contains(name)) continue;   // masquée dans notre barre seulement
                        apps.Add(new PinnedApp(name, file.FullName, KeyOf(target, args, name), IconOf(file.FullName, target, iconLocation), target));
                    }
                }
            }
            catch { /* dossier illisible : liste vide */ }

            // Applications épinglées à notre barre uniquement (clic droit › Épingler à la barre)
            foreach (var exe in Shared.Settings.ExtraPinned.Where(File.Exists))
            {
                string name = Path.GetFileNameWithoutExtension(exe);
                try { var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe); if (!string.IsNullOrWhiteSpace(info.FileDescription)) name = info.FileDescription!.Trim(); } catch { }
                apps.Add(new PinnedApp(name, exe, Path.GetFileNameWithoutExtension(exe).ToLowerInvariant(), IconOf(exe, exe, ""), exe, Extra: true));
            }
            Apps = apps;
            Changed?.Invoke();
        }

        /// <summary>Nom d'exécutable servant à retrouver les fenêtres de l'application (Discord est lancé par « Update.exe --processStart Discord.exe »).</summary>
        private static string KeyOf(string target, string args, string fallback)
        {
            string stem = Path.GetFileNameWithoutExtension(target);
            if (stem.Equals("Update", StringComparison.OrdinalIgnoreCase) && args.Contains("--processStart", StringComparison.OrdinalIgnoreCase))
            {
                var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int i = Array.FindIndex(parts, p => p.Equals("--processStart", StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && i + 1 < parts.Length) stem = Path.GetFileNameWithoutExtension(parts[i + 1]);
            }
            return (stem.Length > 0 ? stem : fallback).ToLowerInvariant();
        }

        // ───────────── Icône du raccourci (celle que Windows affiche) ─────────────

        /// <summary>
        /// Icône de l'application sans la petite flèche de raccourci : celle que le raccourci désigne (IconLocation), sinon celle de
        /// l'exécutable cible. L'icône du fichier .lnk lui-même (avec flèche) n'est qu'un dernier recours.
        /// </summary>
        private ImageSource? IconOf(string lnk, string target, string iconLocation)
        {
            if (_icons.TryGetValue(lnk, out var cached)) return cached;

            string source = target;
            int index = 0;
            if (!string.IsNullOrWhiteSpace(iconLocation) && iconLocation != ",0")
            {
                int comma = iconLocation.LastIndexOf(',');
                string file = comma > 0 ? iconLocation.Substring(0, comma) : iconLocation;
                if (comma > 0) int.TryParse(iconLocation.Substring(comma + 1), out index);
                file = Environment.ExpandEnvironmentVariables(file.Trim('"'));
                if (File.Exists(file)) source = file;
            }

            ImageSource? result = null;
            try
            {
                if (source.Length > 0 && File.Exists(source))
                {
                    result = IconExtractor.FromFile(source, index);   // 128 px : reste net quand les icônes sont grandes
                    var large = new IntPtr[1];
                    if (result == null && ExtractIconEx(source, index, large, null, 1) > 0 && large[0] != IntPtr.Zero)
                    {
                        var bitmap = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bitmap.Freeze();
                        DestroyIcon(large[0]);
                        result = bitmap;
                    }
                }
                // Dernier recours : l'icône du fichier .lnk porte une flèche de raccourci. On dessine plutôt une pastille avec l'initiale.
                result ??= Initial(lnk);
            }            catch { }
            _icons[lnk] = result;
            return result;
        }

        /// <summary>Pastille colorée avec la première lettre du nom : repère reconnaissable quand l'icône de l'application est introuvable.</summary>
        private static ImageSource Initial(string lnkPath)
        {
            string name = Path.GetFileNameWithoutExtension(lnkPath);
            int hash = name.Aggregate(17, (h, c) => h * 31 + c);
            var color = Color.FromRgb((byte)(70 + Math.Abs(hash) % 110), (byte)(70 + Math.Abs(hash / 7) % 110), (byte)(90 + Math.Abs(hash / 13) % 110));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(2, 2, 60, 60), 14, 14);
                var text = new FormattedText(name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 36, Brushes.White, 1.0);
                dc.DrawText(text, new Point(32 - text.Width / 2, 32 - text.Height / 2));
            }
            var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attrs; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        // ───────────── Fusion avec les fenêtres ouvertes ─────────────

        /// <summary>Épinglées d'abord (ouvertes ou non), puis les fenêtres qui ne correspondent à aucune épingle.</summary>
        public static List<TaskbarItem> Merge(IReadOnlyList<PinnedApp> pinned, IEnumerable<AppWindow> windows, bool includePinned)
        {
            var list = windows.ToList();
            var used = new HashSet<IntPtr>();
            var items = new List<TaskbarItem>();

            if (includePinned)
            {
                foreach (var p in pinned)
                {
                    var mine = list.Where(w => !used.Contains(w.Handle) &&
                        (Path.GetFileNameWithoutExtension(w.ExePath).Equals(p.Key, StringComparison.OrdinalIgnoreCase) || w.App.Equals(p.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                    foreach (var w in mine) used.Add(w.Handle);
                    items.Add(new TaskbarItem(p.Name, p.Icon, true, p.LnkPath, mine, p.Target, p.Extra, p.Extra ? p.LnkPath : System.IO.Path.GetFileNameWithoutExtension(p.LnkPath)));
                }
            }
            foreach (var w in list.Where(w => !used.Contains(w.Handle)))
                items.Add(new TaskbarItem(w.App, w.Icon, false, "", new[] { w }, w.ExePath));
            return items;
        }
    }
}




