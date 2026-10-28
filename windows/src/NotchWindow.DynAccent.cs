using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Navbar.Services;

namespace Navbar
{
    // Accent tiré du fond d'écran ou de l'application active (options de Réglages › Apparence). La pochette garde la priorité quand une musique joue.
    public partial class NotchWindow
    {
        private Color? _dynAccent;
        private string _wallpaperKey = "";
        private readonly DispatcherTimer _wallpaperTimer = new() { Interval = TimeSpan.FromSeconds(30) };

        private bool UsesDynAccent => _settings.AccentFromWallpaper || _settings.AccentFromApp;

        private void InitDynAccent()
        {
            _wallpaperTimer.Tick += (_, _) => UpdateDynAccent();
            Closed += (_, _) => _wallpaperTimer.Stop();
            UpdateDynAccent();
        }

        /// <summary>Recalcule l'accent dynamique selon les options ; l'application active passe avant le fond d'écran.</summary>
        private void UpdateDynAccent()
        {
            if (!UsesDynAccent)
            {
                _wallpaperTimer.Stop();
                if (_dynAccent != null) { _dynAccent = null; _wallpaperKey = ""; ApplyAccent(); RefreshPill(); }
                return;
            }

            Color? next = null;
            if (_settings.AccentFromApp && Shared.Windows.Windows.FirstOrDefault(w => w.Active)?.Icon is BitmapSource icon)
                next = DominantColor(icon);
            if (next == null && _settings.AccentFromWallpaper)
            {
                if (!_wallpaperTimer.IsEnabled) _wallpaperTimer.Start();
                next = WallpaperColor();
            }
            else if (!_settings.AccentFromWallpaper) _wallpaperTimer.Stop();

            if (next == _dynAccent) return;
            _dynAccent = next;
            ApplyAccent();
            RefreshPill();
        }

        private Color? _wallpaperColor;

        /// <summary>Couleur dominante du fond d'écran, relue seulement quand le fichier change.</summary>
        private Color? WallpaperColor()
        {
            try
            {
                string path = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", "") as string ?? "";
                if (path.Length == 0 || !File.Exists(path))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (!File.Exists(path)) return null;

                string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
                if (key == _wallpaperKey) return _wallpaperColor;
                _wallpaperKey = key;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 64;   // on ne garde qu'une miniature : la couleur dominante n'a besoin de rien de plus
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                _wallpaperColor = DominantColor(bmp);
            }
            catch { _wallpaperColor = null; }
            return _wallpaperColor;
        }
    }
}
