using System;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Forms = System.Windows.Forms;

namespace Navbar.Services
{
    public sealed record OverlayInfo(string Glyph, double Value, string Label, string Kind = "");

    /// <summary>Surveille volume et batterie et déclenche un événement quand un overlay doit s'afficher.</summary>
    public sealed class SystemOverlayMonitor
    {
        public event Action<OverlayInfo>? OverlayRequested;

        private readonly VolumeService _volume = new();
        public VolumeService Volume => _volume;
        public event Action<float, bool>? VolumeChanged;   // sert à garder le curseur de volume à jour
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

        private bool _volumeKnown;
        private float _lastVolume;
        private bool _lastMuted;

        private bool _batteryKnown;
        private Forms.PowerLineStatus _lastLine;
        private int _lastPercent;
        private DateTime _lastBatteryCheck = DateTime.MinValue;

        public SystemOverlayMonitor()
        {
            _timer.Tick += (_, _) => Poll();
        }

        // Luminosité (écrans internes de portable uniquement ; WMI renvoie vide sinon)
        private bool _brightnessAvailable = true;
        private int _lastBrightness = -1;
        private DateTime _lastBrightnessCheck = DateTime.MinValue;

        private void PollBrightness()
        {
            if (!_brightnessAvailable) return;
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                int value = -1;
                foreach (System.Management.ManagementObject o in searcher.Get())
                {
                    value = Convert.ToInt32(o["CurrentBrightness"]);
                    break;
                }
                if (value < 0) { _brightnessAvailable = false; return; }

                if (_lastBrightness >= 0 && value != _lastBrightness)
                    OverlayRequested?.Invoke(new OverlayInfo("", value / 100.0, $"{value}%", "brightness"));
                _lastBrightness = value;
            }
            catch { _brightnessAvailable = false; }
        }

        public void Start()
        {
            _timer.Start();
            StartHeadphoneWatcher();
        }

        public void Stop()
        {
            _timer.Stop();
            StopHeadphoneWatcher();
        }

        private void Poll()
        {
            PollVolume();
            PollKeyboard();
            if (_brightnessAvailable && DateTime.UtcNow - _lastBrightnessCheck > TimeSpan.FromMilliseconds(1200))
            {
                _lastBrightnessCheck = DateTime.UtcNow;
                PollBrightness();
            }
            if (DateTime.UtcNow - _lastBatteryCheck > TimeSpan.FromSeconds(2))
            {
                _lastBatteryCheck = DateTime.UtcNow;
                PollBattery();
                PollWifi();
            }
        }

        // ───────────── Verr Maj et langue du clavier ─────────────

        private bool _capsKnown, _lastCaps;
        private IntPtr _lastForeground;
        private int _lastLangId = -1;

        private void PollKeyboard()
        {
            bool caps = (GetKeyState(0x14 /*VK_CAPITAL*/) & 1) != 0;
            if (_capsKnown && caps != _lastCaps)
                OverlayRequested?.Invoke(new OverlayInfo("\uE74A", caps ? 1 : 0, caps ? "Verr Maj activée" : "Verr Maj désactivée", "caps"));
            _capsKnown = true;
            _lastCaps = caps;

            // Langue du clavier de la fenêtre active ; on ignore les changements dus à un simple changement de fenêtre
            IntPtr fg = GetForegroundWindow();
            uint thread = GetWindowThreadProcessId(fg, out _);
            int lang = (int)((ulong)GetKeyboardLayout(thread) & 0xFFFF);
            if (fg == _lastForeground && _lastLangId >= 0 && lang != _lastLangId)
            {
                try
                {
                    var culture = CultureInfo.GetCultureInfo(lang);
                    string name = culture.TwoLetterISOLanguageName.ToUpperInvariant();
                    OverlayRequested?.Invoke(new OverlayInfo("\uE774", 1, $"{name} · {culture.NativeName}", "layout"));
                }
                catch (CultureNotFoundException) { }
            }
            _lastForeground = fg;
            _lastLangId = lang;
        }

        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);

        // ───────────── Wi-Fi ─────────────

        private bool _wifiKnown, _lastWifi;

        private void PollWifi()
        {
            try
            {
                var wifi = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToList();
                if (wifi.Count == 0) return;   // pas de Wi-Fi sur cette machine

                bool up = wifi.Any(n => n.OperationalStatus == OperationalStatus.Up);
                if (_wifiKnown && up != _lastWifi)
                    OverlayRequested?.Invoke(new OverlayInfo("\uE701", up ? 1 : 0, up ? "Wi-Fi connecté" : "Wi-Fi perdu", "wifi"));
                _wifiKnown = true;
                _lastWifi = up;
            }
            catch { /* adaptateurs indisponibles pendant un changement de réseau */ }
        }

        // ───────────── Casque / périphériques audio ─────────────

        private DeviceWatcher? _audioWatcher;
        private bool _audioReady;

        private void StartHeadphoneWatcher()
        {
            if (_audioWatcher != null) return;
            try
            {
                _audioReady = false;
                _audioWatcher = DeviceInformation.CreateWatcher(DeviceClass.AudioRender);
                // Au démarrage Windows énumère tous les appareils existants : on ne notifie qu'après cette phase
                _audioWatcher.EnumerationCompleted += (_, _) => _audioReady = true;
                _audioWatcher.Added += (_, info) => AudioDeviceChanged(info.Name, true);
                _audioWatcher.Removed += (_, info) => AudioDeviceChanged(null, false);
                _audioWatcher.Start();
            }
            catch { _audioWatcher = null; }
        }

        private void StopHeadphoneWatcher()
        {
            try
            {
                if (_audioWatcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
                    _audioWatcher.Stop();
            }
            catch { }
            _audioWatcher = null;
        }

        private static readonly string[] HeadphoneWords = { "casque", "headphone", "headset", "écouteur", "ecouteur", "earbud", "airpods", "buds" };

        private void AudioDeviceChanged(string? name, bool added)
        {
            if (!_audioReady) return;
            bool headphones = name != null && HeadphoneWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
            string what = headphones ? "Casque" : "Audio";
            string label = added ? $"{what} connecté · {name}" : $"{what} déconnecté";
            Application.Current?.Dispatcher.BeginInvoke(() =>
                OverlayRequested?.Invoke(new OverlayInfo("\uE7F6", added ? 1 : 0, label, "audio")));
        }

        private void PollVolume()
        {
            if (!_volume.TryRead(out float v, out bool muted)) return;

            bool changed = !_volumeKnown || Math.Abs(v - _lastVolume) > 0.004f || muted != _lastMuted;
            if (changed) VolumeChanged?.Invoke(v, muted);

            if (_volumeKnown && changed)
            {
                string glyph = muted || v <= 0.001f ? "" : v < 0.34f ? "" : v < 0.67f ? "" : "";
                OverlayRequested?.Invoke(new OverlayInfo(glyph, muted ? 0 : v, muted ? "Muet" : $"{Math.Round(v * 100)}%", "volume"));
            }
            _volumeKnown = true;
            _lastVolume = v;
            _lastMuted = muted;
        }

        private void PollBattery()
        {
            var ps = Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)) return;

            int percent = (int)Math.Round(Math.Clamp(ps.BatteryLifePercent, 0f, 1f) * 100);
            var line = ps.PowerLineStatus;

            if (_batteryKnown)
            {
                if (line != _lastLine)
                {
                    bool charging = line == Forms.PowerLineStatus.Online;
                    OverlayRequested?.Invoke(new OverlayInfo(
                        charging ? "" : "", percent / 100.0,
                        charging ? $"En charge · {percent}%" : $"Sur batterie · {percent}%"));
                }
                else if (line != Forms.PowerLineStatus.Online && percent != _lastPercent && (percent == 20 || percent == 10 || percent == 5))
                {
                    OverlayRequested?.Invoke(new OverlayInfo("", percent / 100.0, $"Batterie faible · {percent}%"));
                }
            }
            _batteryKnown = true;
            _lastLine = line;
            _lastPercent = percent;
        }
    }
}


