using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace Navbar.Services
{
    public sealed record PingResult(string Label, int? Ms);

    public sealed record NetSnapshot(double DownBps, double UpBps, string Adapter, string Kind, string Ssid, bool Online, IReadOnlyList<PingResult> Pings);

    /// <summary>Débit en direct, connexion active, latence vers quelques cibles et détection de coupure.</summary>
    public sealed class NetworkService
    {
        private long _lastRx, _lastTx;
        private DateTime _lastAt = DateTime.MinValue;
        private DateTime _ssidAt = DateTime.MinValue;
        private string _ssid = "";
        private int _failures;

        public bool Online { get; private set; } = true;
        public List<double> DownHistory { get; } = new();

        private static bool Usable(NetworkInterface n) =>
            n.OperationalStatus == OperationalStatus.Up
            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            && !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase)
            && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase);

        /// <summary>Un relevé : débit depuis le précédent, pings (complets ou seulement la cible principale) et état de la connexion.</summary>
        public async Task<NetSnapshot> SampleAsync(bool fullPings)
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces().Where(Usable).ToList();
            var active = nics.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                         ?? nics.FirstOrDefault();

            long rx = nics.Sum(n => n.GetIPv4Statistics().BytesReceived), tx = nics.Sum(n => n.GetIPv4Statistics().BytesSent);
            var now = DateTime.UtcNow;
            double seconds = (now - _lastAt).TotalSeconds, down = 0, up = 0;
            if (_lastAt != DateTime.MinValue && seconds > 0.2 && rx >= _lastRx && tx >= _lastTx)
            {
                down = (rx - _lastRx) / seconds;
                up = (tx - _lastTx) / seconds;
            }
            _lastRx = rx; _lastTx = tx; _lastAt = now;
            DownHistory.Add(down);
            if (DownHistory.Count > 40) DownHistory.RemoveAt(0);

            string gateway = active?.GetIPProperties().GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "";
            var targets = new List<(string Label, string Host)> { ("Cloudflare", "1.1.1.1") };
            if (fullPings)
            {
                targets.Add(("Google", "8.8.8.8"));
                if (gateway.Length > 0) targets.Add(("Box / routeur", gateway));
            }
            var pings = await Task.WhenAll(targets.Select(async t => new PingResult(t.Label, await PingAsync(t.Host))));

            bool reachable = pings.Any(p => p.Ms != null) || (!fullPings && await PingAsync("8.8.8.8") != null);
            _failures = reachable ? 0 : _failures + 1;
            Online = _failures < 2;   // deux relevés ratés d'affilée : la connexion est considérée comme coupée

            bool wifi = active?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            if (wifi && DateTime.UtcNow - _ssidAt > TimeSpan.FromSeconds(20)) { _ssid = await ReadSsidAsync(); _ssidAt = DateTime.UtcNow; }
            return new NetSnapshot(down, up, active?.Name ?? "Aucune connexion", wifi ? "Wi-Fi" : active != null ? "Ethernet" : "", wifi ? _ssid : "", Online, pings);
        }

        private static async Task<int?> PingAsync(string host)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 1200);
                return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
            }
            catch { return null; }
        }

        private static async Task<string> ReadSsidAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi)!;
                string output = await p.StandardOutput.ReadToEndAsync();
                foreach (var line in output.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.StartsWith("SSID") && !t.StartsWith("SSID name") && !t.StartsWith("BSSID") && t.Contains(':')) return t.Substring(t.IndexOf(':') + 1).Trim();
                }
            }
            catch { }
            return "";
        }

        public static string Rate(double bytesPerSecond) =>
            bytesPerSecond >= 1_048_576 ? $"{bytesPerSecond / 1_048_576:0.0} Mo/s" : bytesPerSecond >= 1024 ? $"{bytesPerSecond / 1024:0} Ko/s" : $"{bytesPerSecond:0} o/s";
    }
}
