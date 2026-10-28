using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;

namespace Navbar.Services
{
    public sealed record WifiNetwork(string Ssid, int Bars, bool Secured, bool Connected);

    /// <summary>Réseaux Wi-Fi à portée, connexion et déconnexion (nécessite une carte Wi-Fi).</summary>
    public sealed class WifiService
    {
        private WiFiAdapter? _adapter;

        /// <summary>Vrai si le PC a une carte Wi-Fi.</summary>
        public async Task<bool> HasAdapterAsync() => await GetAdapterAsync() != null;

        private async Task<WiFiAdapter?> GetAdapterAsync()
        {
            if (_adapter != null) return _adapter;
            try
            {
                if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed) return null;
                var adapters = await WiFiAdapter.FindAllAdaptersAsync();
                _adapter = adapters.FirstOrDefault();
            }
            catch { _adapter = null; }
            return _adapter;
        }

        public async Task<List<WifiNetwork>> ScanAsync()
        {
            var result = new List<WifiNetwork>();
            var adapter = await GetAdapterAsync();
            if (adapter == null) return result;

            try
            {
                await adapter.ScanAsync();
                string? current = await ConnectedSsidAsync(adapter);

                foreach (var n in adapter.NetworkReport.AvailableNetworks)
                {
                    if (string.IsNullOrWhiteSpace(n.Ssid)) continue;
                    bool secured = n.SecuritySettings.NetworkAuthenticationType != Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211;
                    result.Add(new WifiNetwork(n.Ssid, n.SignalBars, secured, n.Ssid == current));
                }
            }
            catch { /* analyse refusée ou carte en veille */ }

            // Un SSID peut apparaître plusieurs fois (plusieurs bornes) : on garde le plus fort
            return result.GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.Bars).First())
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Bars)
                .ToList();
        }

        private static async Task<string?> ConnectedSsidAsync(WiFiAdapter adapter)
        {
            try
            {
                var profile = await adapter.NetworkAdapter.GetConnectedProfileAsync();
                return profile?.WlanConnectionProfileDetails?.GetConnectedSsid();
            }
            catch { return null; }
        }

        /// <summary>Se connecte à un réseau déjà connu de Windows (profil enregistré) ; renvoie un message pour l'utilisateur.</summary>
        public async Task<string> ConnectAsync(string ssid)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", $"wlan connect name=\"{ssid.Replace("\"", "")}\"")
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8,
                };
                using var process = Process.Start(psi)!;
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return process.ExitCode == 0 && !output.Contains("aucun profil", StringComparison.OrdinalIgnoreCase)
                    ? $"Connexion à {ssid}…"
                    : $"{ssid} : réseau inconnu, connecte-toi d'abord depuis Windows";
            }
            catch { return "Connexion Wi-Fi impossible"; }
        }

        public async Task DisconnectAsync()
        {
            var adapter = await GetAdapterAsync();
            adapter?.Disconnect();
        }
    }
}
