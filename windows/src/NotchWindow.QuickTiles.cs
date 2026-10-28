using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Navbar.Services;
using Windows.Devices.Radios;

namespace Navbar
{
    // Réglages rapides comme dans le centre de contrôle de Windows : Wi-Fi, Bluetooth, mode avion, économiseur de batterie, thème sombre / clair
    public partial class NotchWindow
    {
        private sealed class QuickTile
        {
            public Border Root = null!;
            public TextBlock Glyph = null!, Label = null!, State = null!;
            public bool On;
        }

        private QuickTile? _tileWifi, _tileBluetooth, _tileAirplane, _tileSaver, _tileTheme, _tileHotspot, _tileVpn, _tilePower;
        private System.Collections.Generic.List<RadioInfo> _quickRadios = new();

        private void InitQuickTiles()
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8 * _scale) };
            _tileWifi = AddTile(grid, "", "Wi-Fi", async () => await ToggleRadioAsync(RadioKind.WiFi));
            _tileBluetooth = AddTile(grid, "", "Bluetooth", async () => await ToggleRadioAsync(RadioKind.Bluetooth));
            _tileAirplane = AddTile(grid, "", "Mode avion", async () => await ToggleAirplaneAsync());
            _tileSaver = AddTile(grid, "", "Économiseur", () =>
            {
                // Windows n'expose pas d'interrupteur public : on ouvre sa page dédiée
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:batterysaver") { UseShellExecute = true }); } catch { }
                return Task.CompletedTask;
            });
            _tileTheme = AddTile(grid, "", "Thème", () => { ToggleWindowsTheme(); return Task.CompletedTask; });
            _tileHotspot = AddTile(grid, "\uE88A", "Point d'accès", async () => await ToggleHotspotAsync());
            _tileVpn = AddTile(grid, "\uE705", "VPN", async () => await ToggleVpnAsync());
            _tilePower = AddTile(grid, "\uE945", "Alimentation", async () => await NextPowerPlanAsync());
            AddLinkTile(grid, "\uE708", "Nuit", "ms-settings:nightlight");
            AddLinkTile(grid, "\uE7ED", "Concentration", "ms-settings:quiethours");
            AddLinkTile(grid, "\uEBC6", "Projeter", "displayswitch.exe");
            AddLinkTile(grid, "\uE776", "Accessibilité", "ms-settings:easeofaccess");
            AddLinkTile(grid, "\uE8AF", "À proximité", "ms-settings:crossdevice");
            ControlsList.Children.Insert(0, grid);
        }

        private QuickTile AddTile(Panel parent, string glyph, string label, Func<Task> click)
        {
            var tile = new QuickTile();
            tile.Glyph = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 17 * _scale, HorizontalAlignment = HorizontalAlignment.Left };
            tile.Label = new TextBlock { Text = Loc.T(label), FontSize = 11 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont, Margin = new Thickness(0, 3 * _scale, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            tile.State = new TextBlock { FontSize = 10 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis };
            var stack = new StackPanel();
            stack.Children.Add(tile.Glyph);
            stack.Children.Add(tile.Label);
            stack.Children.Add(tile.State);
            tile.Root = new Border
            {
                Child = stack, CornerRadius = new CornerRadius(12 * _scale), Padding = new Thickness(10 * _scale, 7 * _scale, 8 * _scale, 7 * _scale),
                Margin = new Thickness(3 * _scale), Cursor = Cursors.Hand,
            };
            Press.Attach(tile.Root, 0.96);
            tile.Root.MouseLeftButtonUp += async (_, _) => { await click(); await Task.Delay(250); await RefreshQuickTilesAsync(); };
            parent.Children.Add(tile.Root);
            ApplyTile(tile, false, "…");
            return tile;
        }

        /// <summary>Tuile allumée = couleur d'accent avec texte lisible dessus ; éteinte = fond discret.</summary>
        private void ApplyTile(QuickTile tile, bool on, string state, bool available = true)
        {
            tile.On = on;
            var accent = EffectiveAccentColor();
            double luma = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
            Brush onFg = luma > 0.6 ? Brushes.Black : Brushes.White;
            tile.Root.Background = on ? new SolidColorBrush(accent) : FgAlpha(0x14);
            tile.Root.Opacity = available ? 1 : 0.45;
            tile.Glyph.Foreground = tile.Label.Foreground = on ? onFg : Fg();
            tile.State.Foreground = on ? onFg : FgAlpha(0x88);
            string shown = Loc.T(state);
            if (tile.State.Text != shown) tile.State.Text = shown;   // pas de réécriture inutile : le texte ne clignote pas
        }

        private async Task RefreshQuickTilesAsync()
        {
            if (_tileWifi == null) return;
            _quickRadios = await Shared.Radios.GetAsync();
            var wifi = _quickRadios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            var bluetooth = _quickRadios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            ApplyTile(_tileWifi, wifi?.On == true, wifi == null ? "Indisponible" : wifi.On ? "Activé" : "Désactivé", wifi != null);
            ApplyTile(_tileBluetooth!, bluetooth?.On == true, bluetooth == null ? "Indisponible" : bluetooth.On ? "Activé" : "Désactivé", bluetooth != null);

            bool airplane = _quickRadios.Count > 0 && _quickRadios.All(r => !r.On);
            ApplyTile(_tileAirplane!, airplane, airplane ? "Activé" : "Désactivé", _quickRadios.Count > 0);

            string saver = "";
            bool saverOn = false;
            try
            {
                var status = Windows.System.Power.PowerManager.EnergySaverStatus;
                saverOn = status == Windows.System.Power.EnergySaverStatus.On;
                saver = status switch { Windows.System.Power.EnergySaverStatus.On => "Activé", Windows.System.Power.EnergySaverStatus.Off => "Désactivé", _ => "Indisponible" };
            }
            catch { saver = "Indisponible"; }
            ApplyTile(_tileSaver!, saverOn, saver);

            bool light = SystemAppsUseLightTheme();
            ApplyTile(_tileTheme!, light, light ? "Clair" : "Sombre");
            var manager = HotspotManager();
            bool hotspot = manager?.TetheringOperationalState == Windows.Networking.NetworkOperators.TetheringOperationalState.On;
            ApplyTile(_tileHotspot!, hotspot, manager == null ? "Indisponible" : hotspot ? "Activé" : "Désactivé", manager != null);

            var (vpnText, vpnOn, vpnAvailable) = await Task.Run(() =>
            {
                var profiles = VpnProfiles();
                if (profiles.Count == 0) return ("Aucun profil", false, false);
                var active = profiles.FirstOrDefault(p => RunHidden("rasdial.exe", "").Contains(p, StringComparison.OrdinalIgnoreCase));
                return (active ?? profiles[0], active != null, true);
            });
            ApplyTile(_tileVpn!, vpnOn, vpnOn ? vpnText : vpnAvailable ? "Désactivé" : vpnText, vpnAvailable);

            var plans = await Task.Run(PowerPlans);
            var current = plans.FirstOrDefault(p => p.Active);
            ApplyTile(_tilePower!, false, current.Name ?? "Indisponible", plans.Count > 1);
        }

        private async Task ToggleRadioAsync(RadioKind kind)
        {
            var radio = _quickRadios.FirstOrDefault(r => r.Kind == kind);
            if (radio != null) await Shared.Radios.SetAsync(radio, !radio.On);
        }

        /// <summary>Mode avion : coupe le Wi-Fi et le Bluetooth ; s'il est déjà actif, les rallume.</summary>
        private async Task ToggleAirplaneAsync()
        {
            bool allOff = _quickRadios.Count > 0 && _quickRadios.All(r => !r.On);
            foreach (var radio in _quickRadios) await Shared.Radios.SetAsync(radio, allOff);
        }


        /// <summary>Tuile sans état : ouvre la page ou l'outil Windows correspondant (réglages sans interrupteur public).</summary>
        private void AddLinkTile(Panel parent, string glyph, string label, string target)
        {
            var tile = AddTile(parent, glyph, label, () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
                return Task.CompletedTask;
            });
            ApplyTile(tile, false, "Ouvrir");
            tile.Root.Tag = "link";   // gardée telle quelle lors des rafraîchissements
        }

        // ───────────── Point d'accès mobile ─────────────

        private static Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager? HotspotManager()
        {
            try
            {
                var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                return profile == null ? null : Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            }
            catch { return null; }
        }

        private static async Task ToggleHotspotAsync()
        {
            try
            {
                var manager = HotspotManager();
                if (manager == null) return;
                if (manager.TetheringOperationalState == Windows.Networking.NetworkOperators.TetheringOperationalState.On) await manager.StopTetheringAsync();
                else await manager.StartTetheringAsync();
            }
            catch { }
        }

        // ───────────── VPN (connexions Windows, via rasdial) ─────────────

        private static System.Collections.Generic.List<string> VpnProfiles()
        {
            var names = new System.Collections.Generic.List<string>();
            try
            {
                string pbk = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk");
                if (!System.IO.File.Exists(pbk)) return names;
                foreach (var line in System.IO.File.ReadLines(pbk))
                    if (line.StartsWith("[") && line.TrimEnd().EndsWith("]")) names.Add(line.Trim().Trim('[', ']'));
            }
            catch { }
            return names;
        }

        private static string RunHidden(string file, string args)
        {
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);   // pages de code OEM (sortie de powercfg / rasdial)
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage) });
                string output = process!.StandardOutput.ReadToEnd();
                process.WaitForExit(8000);
                return output;
            }
            catch { return ""; }
        }

        private static async Task ToggleVpnAsync()
        {
            var profiles = VpnProfiles();
            if (profiles.Count == 0) return;
            await Task.Run(() =>
            {
                string connected = RunHidden("rasdial.exe", "");
                var active = profiles.FirstOrDefault(p => connected.Contains(p, StringComparison.OrdinalIgnoreCase));
                if (active != null) RunHidden("rasdial.exe", $"\"{active}\" /disconnect");
                else RunHidden("rasdial.exe", $"\"{profiles[0]}\"");
            });
        }

        // ───────────── Mode d'alimentation ─────────────

        private static System.Collections.Generic.List<(string Guid, string Name, bool Active)> PowerPlans()
        {
            var plans = new System.Collections.Generic.List<(string, string, bool)>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(RunHidden("powercfg.exe", "/list"), @"([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})\s+\((.+?)\)(\s*\*)?"))
                plans.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success));
            return plans;
        }

        /// <summary>Passe au plan d'alimentation suivant (équilibré, économie d'énergie, performances…).</summary>
        private static async Task NextPowerPlanAsync()
        {
            await Task.Run(() =>
            {
                var plans = PowerPlans();
                if (plans.Count < 2) return;
                int active = Math.Max(0, plans.FindIndex(p => p.Active));
                RunHidden("powercfg.exe", "/setactive " + plans[(active + 1) % plans.Count].Guid);
            });
        }
        // ───────────── Thème de Windows ─────────────

        private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private static bool SystemAppsUseLightTheme() => Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 0) is int v && v == 1;

        /// <summary>Bascule les applications et le système de Windows entre clair et sombre, puis prévient les fenêtres ouvertes.</summary>
        private static void ToggleWindowsTheme()
        {
            try
            {
                int next = SystemAppsUseLightTheme() ? 0 : 1;
                Registry.SetValue(PersonalizeKey, "AppsUseLightTheme", next, RegistryValueKind.DWord);
                Registry.SetValue(PersonalizeKey, "SystemUsesLightTheme", next, RegistryValueKind.DWord);
                SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet", 2 /* SMTO_ABORTIFHUNG */, 100, out _);
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);
    }
}
