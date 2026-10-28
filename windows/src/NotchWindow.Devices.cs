using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Navbar.Services;
using Forms = System.Windows.Forms;

namespace Navbar
{
    // Appareils Bluetooth (connexion) et batteries (ordinateur + appareils)
    public partial class NotchWindow
    {
        private BluetoothService _bt => Shared.Bluetooth;
        private RadioService _radios => Shared.Radios;
        private System.Collections.Generic.List<RadioInfo> _radioList = new();
        private System.Collections.Generic.List<WifiNetwork> _wifiNetworks = new();
        private bool _hasWifi;
        private List<BtDevice> _btDevices = new();
        private Dictionary<string, int> _btBattery = new(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer? _devicesTimer, _batteryTimer;
        private bool _devicesBusy;

        private void InitDevices()
        {
            // Relevé de fond des batteries pour l'en-tête et la pastille (léger : une fois toutes les 3 minutes)
            _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            _batteryTimer.Tick += async (_, _) => { if (WantBackgroundBattery) await RefreshDevicesAsync(false); };
            _batteryTimer.Start();
            Closed += (_, _) => { _batteryTimer?.Stop(); _devicesTimer?.Stop(); };
            UpdateBatteryDisplays();
            Loaded += async (_, _) =>
            {
                await Task.Delay(6000);
                if (WantBackgroundBattery) await RefreshDevicesAsync(false);
            };
        }

        private bool WantBackgroundBattery => _settings.ShowDevices && (_settings.HeaderBattery || _settings.PillBattery);

        private void DevicesToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Devices);

        // ───────────── Rafraîchissement ─────────────

        private void StartDevicesRefresh()
        {
            if (_devicesTimer != null) return;
            _devicesTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _devicesTimer.Tick += async (_, _) => await RefreshDevicesAsync(true);
            _devicesTimer.Start();
            _ = RefreshDevicesAsync(true);
        }

        private void StopDevicesRefresh()
        {
            _devicesTimer?.Stop();
            _devicesTimer = null;
        }

        private async Task RefreshDevicesAsync(bool render)
        {
            if (_devicesBusy) return;
            _devicesBusy = true;
            try
            {
                _radioList = await _radios.GetAsync();
                _hasWifi = await Shared.Wifi.HasAdapterAsync();
                _btDevices = await Task.Run(() => _bt.GetDevices());
                if (render) RenderDevices();

                // Les réseaux Wi-Fi à portée (analyse un peu longue) ne sont cherchés que pour l'onglet ouvert
                if (render && _hasWifi && _radioList.Any(r => r.Kind == Windows.Devices.Radios.RadioKind.WiFi && r.On))
                {
                    _wifiNetworks = await Shared.Wifi.ScanAsync();
                    if (_view == HoverView.Devices) RenderDevices();
                }

                // La batterie vient d'une énumération plus lente : on l'ajoute dès qu'elle arrive
                // L'énumération PnP est lourde : on la traite hors du thread d'interface pour ne jamais figer la pastille
                _btBattery = await Task.Run(() => _bt.GetBatteriesAsync());
                if (render && _view == HoverView.Devices) RenderDevices();
                UpdateBatteryDisplays();
            }
            catch { /* pas de radio Bluetooth ou pilote indisponible */ }
            finally { _devicesBusy = false; }
        }

        // ───────────── Affichage ─────────────

        private void RenderDevices()
        {
            RenderPcBattery();
            RenderRadios();
            RenderWifi();

            DevicesList.Children.Clear();
            if (_btDevices.Count == 0)
            {
                DevicesList.Children.Add(new TextBlock
                {
                    Text = "Aucun appareil Bluetooth appairé", Foreground = FgAlpha(0x77), FontSize = 12 * _scale,
                    FontFamily = UiFont, Margin = new Thickness(0, 6 * _scale, 0, 0),
                });
                return;
            }
            foreach (var device in _btDevices) DevicesList.Children.Add(BuildDeviceRow(device));
        }

        /// <summary>Interrupteurs Bluetooth et Wi-Fi de la machine.</summary>
        private void RenderRadios()
        {
            RadiosRow.Children.Clear();
            foreach (var radio in _radioList)
            {
                string name = radio.Kind == Windows.Devices.Radios.RadioKind.Bluetooth ? "Bluetooth" : "Wi-Fi";
                string glyph = radio.Kind == Windows.Devices.Radios.RadioKind.Bluetooth ? "\uE702" : "\uE701";
                var chip = Chip($"{name} · {(radio.On ? "activé" : "désactivé")}", radio.On, async () =>
                {
                    bool ok = await _radios.SetAsync(radio, !radio.On);
                    ShowOverlay(new OverlayInfo(glyph, radio.On ? 0 : 1, ok ? $"{name} {(radio.On ? "désactivé" : "activé")}" : $"{name} : accès refusé", "radio"));
                    await Task.Delay(800);
                    await RefreshDevicesAsync(true);
                });
                RadiosRow.Children.Add(chip);
            }
            RadiosRow.Margin = new Thickness(0, 0, 0, 4 * _scale);
        }

        /// <summary>État du réseau et, s'il y a une carte Wi-Fi, les réseaux à portée avec connexion en un clic.</summary>
        private void RenderWifi()
        {
            WifiList.Children.Clear();
            WifiList.Margin = new Thickness(0, 0, 0, 4 * _scale);

            string wired = ActiveNetworkName();
            if (!_hasWifi)
            {
                WifiList.Children.Add(new TextBlock
                {
                    Text = wired.Length > 0 ? $"Réseau : {wired} · pas de carte Wi-Fi sur ce PC" : "Pas de carte Wi-Fi sur ce PC",
                    Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, Margin = new Thickness(0, 0, 0, 4 * _scale),
                });
                return;
            }

            foreach (var network in _wifiNetworks.Take(4))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 3 * _scale) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var glyph = new TextBlock
                {
                    Text = "\uE701", FontFamily = IconFont, FontSize = 15 * _scale, Width = 28 * _scale, TextAlignment = TextAlignment.Center,
                    Foreground = network.Connected ? EffectiveAccent() : FgAlpha((byte)(0x55 + network.Bars * 0x22)), VerticalAlignment = VerticalAlignment.Center,
                };
                var name = new TextBlock
                {
                    Text = network.Ssid + (network.Secured ? "  🔒" : ""), Foreground = Fg(), FontSize = 12 * _scale, FontFamily = UiFont,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(name, 1);

                var ssid = network.Ssid;
                var button = (FrameworkElement)Chip(network.Connected ? "Déconnecter" : "Connecter", false, async () =>
                {
                    if (network.Connected) await Shared.Wifi.DisconnectAsync();
                    else ShowOverlay(new OverlayInfo("\uE701", 1, await Shared.Wifi.ConnectAsync(ssid), "wifi"));
                    await Task.Delay(2500);
                    await RefreshDevicesAsync(true);
                });
                button.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(button, 2);

                row.Children.Add(glyph);
                row.Children.Add(name);
                row.Children.Add(button);
                WifiList.Children.Add(row);
            }
        }

        private static string ActiveNetworkName()
        {
            try
            {
                var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                         && n.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                                             or System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211
                                         && !n.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase));
                return nic == null ? "" : nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ? "Ethernet" : "Wi-Fi";
            }
            catch { return ""; }
        }

        private void RenderPcBattery()
        {
            PcBatteryRow.Children.Clear();
            PcBatteryRow.ColumnDefinitions.Clear();
            PcBatteryRow.Margin = new Thickness(0, 0, 8 * _scale, 4 * _scale);
            PcBatteryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            PcBatteryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ps = Forms.SystemInformation.PowerStatus;
            bool hasBattery = !ps.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery);
            int percent = (int)Math.Round(Math.Clamp(ps.BatteryLifePercent, 0f, 1f) * 100);
            bool charging = ps.PowerLineStatus == Forms.PowerLineStatus.Online;

            var glyph = new TextBlock
            {
                Text = !hasBattery ? "" : charging ? "" : "", Foreground = Fg(), FontFamily = IconFont,
                FontSize = 17 * _scale, Width = 28 * _scale, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = hasBattery ? $"Ordinateur · {percent} %{(charging ? " · en charge" : "")}" : "Ordinateur · secteur",
                Foreground = Fg(), FontSize = 13 * _scale, FontFamily = UiFont, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 1);
            PcBatteryRow.Children.Add(glyph);
            PcBatteryRow.Children.Add(label);
        }

        private UIElement BuildDeviceRow(BtDevice device)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4 * _scale), Background = System.Windows.Media.Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = new TextBlock
            {
                Text = DeviceGlyph(device), FontFamily = IconFont, FontSize = 16 * _scale, Width = 28 * _scale,
                Foreground = device.Connected ? EffectiveAccent() : FgAlpha(0x88),
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };

            string status = device.Connected ? "Connecté" : "Appairé";
            if (device.Connected && _btBattery.TryGetValue(device.Name, out int pct)) status += $" · {pct} %";

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = device.Name, Foreground = Fg(), FontSize = 13 * _scale, FontFamily = UiFont,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock { Text = status, Foreground = FgAlpha(0x88), FontSize = 11 * _scale, FontFamily = UiFont });
            Grid.SetColumn(text, 1);

            var button = (FrameworkElement)Chip(device.Connected ? "Déconnecter" : "Connecter", false, () => ToggleDevice(device));
            button.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(button, 2);

            row.Children.Add(glyph);
            row.Children.Add(text);
            row.Children.Add(button);
            return row;
        }

        private static string DeviceGlyph(BtDevice d) =>
            d.IsAudio ? "" : d.IsKeyboard ? "" : d.IsMouse ? "" : d.Major == 2 ? "" : "";

        private async void ToggleDevice(BtDevice device)
        {
            bool connect = !device.Connected;
            ShowOverlay(new OverlayInfo(DeviceGlyph(device), 1, (connect ? "Connexion · " : "Déconnexion · ") + device.Name, "bluetooth"));
            await Task.Run(() => _bt.SetConnected(device, connect));
            await Task.Delay(1500);   // laisse à Windows le temps d'établir ou de couper la liaison
            await RefreshDevicesAsync(true);
        }

        // ───────────── Batterie dans l'en-tête et la pastille ─────────────

        private IEnumerable<(string Glyph, string Text)> BatteryItems()
        {
            var ps = Forms.SystemInformation.PowerStatus;
            if (!ps.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
            {
                bool charging = ps.PowerLineStatus == Forms.PowerLineStatus.Online;
                yield return (charging ? "" : "", $"{Math.Round(Math.Clamp(ps.BatteryLifePercent, 0f, 1f) * 100)} %");
            }

            foreach (var d in _btDevices.Where(d => d.Connected))
                if (_btBattery.TryGetValue(d.Name, out int pct)) yield return (DeviceGlyph(d), $"{pct} %");
        }

        /// <summary>Remplit un TextBlock avec « icône + pourcentage » pour chaque batterie connue ; vrai s'il y en a.</summary>
        private bool FillBattery(TextBlock target)
        {
            target.Inlines.Clear();
            bool any = false;
            foreach (var (glyph, text) in BatteryItems())
            {
                if (any) target.Inlines.Add(new Run("  "));
                target.Inlines.Add(new Run(glyph + " ") { FontFamily = IconFont });
                target.Inlines.Add(new Run(text));
                any = true;
            }
            return any;
        }

        private void UpdateBatteryDisplays()
        {
            bool show = _settings.HeaderBattery && FillBattery(HeaderBattery);
            HeaderBattery.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            RefreshPill();
        }
    }
}
