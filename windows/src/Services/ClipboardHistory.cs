using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Navbar.Services
{
    public enum ClipKind { Text, Image, Files }

    public sealed class ClipEntry
    {
        public ClipKind Kind { get; init; }
        public string Text { get; init; } = "";
        public BitmapSource? Image { get; init; }
        public string[] Files { get; init; } = Array.Empty<string>();
        public DateTime At { get; set; } = DateTime.Now;
        public string Key { get; init; } = "";
        public bool Pinned { get; set; }
    }

    /// <summary>Entrée enregistrée sur disque (texte et fichiers uniquement, jamais les images).</summary>
    public sealed class SavedClip
    {
        public ClipKind Kind { get; set; }
        public string Text { get; set; } = "";
        public string[] Files { get; set; } = Array.Empty<string>();
        public DateTime At { get; set; }
        public bool Pinned { get; set; }
    }

    /// <summary>
    /// Historique du presse-papiers (jusqu'à 10 copies récentes) avec remise en place d'une ancienne copie.
    /// Écoute Windows via AddClipboardFormatListener sur une fenêtre invisible ; ignore les contenus que les
    /// gestionnaires de mots de passe marquent comme sensibles.
    /// </summary>
    public sealed class ClipboardHistory
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int HWND_MESSAGE = -3;

        public event Action? Changed;

        public List<ClipEntry> Entries { get; } = new();
        public int Limit { get; set; } = 10;
        public bool SkipSensitive { get; set; } = true;
        public bool Persist { get; set; }

        private HwndSource? _source;
        private DateTime _ignoreUntil = DateTime.MinValue;

        public bool Running => _source != null;

        public void Start()
        {
            if (_source != null) return;
            var p = new HwndSourceParameters("NavbarClipboard") { ParentWindow = new IntPtr(HWND_MESSAGE), Width = 0, Height = 0 };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            AddClipboardFormatListener(_source.Handle);
        }

        public void Stop()
        {
            if (_source == null) return;
            RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                // Le contenu n'est lisible qu'une fois l'application source repartie : on laisse passer un instant
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                t.Tick += (_, _) => { t.Stop(); Capture(); };
                t.Start();
            }
            return IntPtr.Zero;
        }

        // ───────────── Capture ─────────────

        private void Capture()
        {
            if (DateTime.Now < _ignoreUntil) return;

            try
            {
                var data = Retry(() => Clipboard.GetDataObject());
                if (data == null) return;

                // Les gestionnaires de mots de passe demandent explicitement de ne pas conserver ce contenu
                if (SkipSensitive &&
                    (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") ||
                     data.GetDataPresent("Clipboard Viewer Ignore")))
                    return;

                ClipEntry? entry = null;
                if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
                {
                    if (text.Length > 200_000) text = text.Substring(0, 200_000);
                    entry = new ClipEntry { Kind = ClipKind.Text, Text = text, Key = "t:" + text };
                }
                else if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    entry = new ClipEntry { Kind = ClipKind.Files, Files = files, Key = "f:" + string.Join("|", files) };
                }
                else if (Clipboard.ContainsImage() && Retry(() => Clipboard.GetImage()) is { } image)
                {
                    image.Freeze();
                    entry = new ClipEntry { Kind = ClipKind.Image, Image = image, Key = "i:" + Hash(image) };
                }

                if (entry == null) return;
                Add(entry);
            }
            catch { /* presse-papiers verrouillé par une autre application : on réessaiera à la prochaine copie */ }
        }

        private void Add(ClipEntry entry)
        {
            var same = Entries.FirstOrDefault(e => e.Key == entry.Key);   // une copie identique remonte simplement en tête
            if (same != null) { entry.Pinned = same.Pinned; Entries.Remove(same); }
            Entries.Insert(0, entry);
            SortPinned();
            Trim();
            Save();
            Changed?.Invoke();
        }

        private static string Hash(BitmapSource image)
        {
            int stride = image.PixelWidth * ((image.Format.BitsPerPixel + 7) / 8);
            var pixels = new byte[stride * image.PixelHeight];
            image.CopyPixels(pixels, stride, 0);
            return image.PixelWidth + "x" + image.PixelHeight + ":" + Convert.ToHexString(SHA1.HashData(pixels));
        }

        private static T? Retry<T>(Func<T> read)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { return read(); }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(40); }
            }
            return default;
        }

        // ───────────── Actions ─────────────

        /// <summary>Remet une ancienne copie dans le presse-papiers courant et la place en tête de l'historique.</summary>
        public bool Restore(ClipEntry entry)
        {
            _ignoreUntil = DateTime.Now.AddMilliseconds(700);   // notre propre écriture ne doit pas recréer une entrée
            try
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        switch (entry.Kind)
                        {
                            case ClipKind.Text: Clipboard.SetText(entry.Text); break;
                            case ClipKind.Image: Clipboard.SetImage(entry.Image); break;
                            case ClipKind.Files:
                                var list = new StringCollection();
                                list.AddRange(entry.Files);
                                Clipboard.SetFileDropList(list);
                                break;
                        }
                        break;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        if (attempt == 4) return false;
                        System.Threading.Thread.Sleep(40);
                    }
                }
            }
            catch { return false; }

            Entries.Remove(entry);
            entry.At = DateTime.Now;
            Entries.Insert(0, entry);
            SortPinned();
            Save();
            Changed?.Invoke();
            return true;
        }

        public void Remove(ClipEntry entry)
        {
            Entries.Remove(entry);
            Save();
            Changed?.Invoke();
        }

        public void Clear()
        {
            Entries.Clear();
            Save();
            Changed?.Invoke();
        }

        /// <summary>Les copies épinglées restent en tête et ne comptent pas dans la limite.</summary>
        private void SortPinned()
        {
            var ordered = Entries.Where(e => e.Pinned).Concat(Entries.Where(e => !e.Pinned)).ToList();
            Entries.Clear();
            Entries.AddRange(ordered);
        }

        public void TogglePin(ClipEntry entry)
        {
            entry.Pinned = !entry.Pinned;
            SortPinned();
            Trim();
            Save();
            Changed?.Invoke();
        }

        public void Trim()
        {
            int max = Math.Clamp(Limit, 1, 10);
            var loose = Entries.Where(e => !e.Pinned).ToList();
            foreach (var extra in loose.Skip(max)) Entries.Remove(extra);
        }

        // ───────────── Persistance (facultative) ─────────────

        public void Load()
        {
            foreach (var s in Storage.Load("clipboard", new List<SavedClip>()))
            {
                var entry = s.Kind == ClipKind.Files
                    ? new ClipEntry { Kind = ClipKind.Files, Files = s.Files, At = s.At, Pinned = s.Pinned, Key = "f:" + string.Join("|", s.Files) }
                    : new ClipEntry { Kind = ClipKind.Text, Text = s.Text, At = s.At, Pinned = s.Pinned, Key = "t:" + s.Text };
                Entries.Add(entry);
            }
            Trim();
        }

        private void Save()
        {
            // Sans « garder après redémarrage », seules les copies épinglées (choix explicite) sont conservées sur disque
            Storage.Save("clipboard", Entries.Where(e => e.Kind != ClipKind.Image && (Persist || e.Pinned))
                .Select(e => new SavedClip { Kind = e.Kind, Text = e.Text, Files = e.Files, At = e.At, Pinned = e.Pinned }).ToList());
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}


