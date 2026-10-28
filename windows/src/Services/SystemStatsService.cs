using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Navbar.Services
{
    public sealed record ProcInfo(string Name, double Cpu, long MemoryBytes);

    public sealed record SysSnapshot(
        double Cpu, double RamPercent, long RamUsed, long RamTotal,
        double? Gpu, double? Disk, double NetDown, double NetUp,
        TimeSpan Uptime, double? TempC,
        IReadOnlyList<ProcInfo> TopCpu, IReadOnlyList<ProcInfo> TopMemory, int? PingMs = null);

    /// <summary>
    /// Mesure des ressources : processeur, mémoire, carte graphique, disque, réseau et processus les plus gourmands.
    /// Chaque mesure est calculée à partir de la précédente : un seul échantillonneur doit exister (voir Shared).
    /// </summary>
    public sealed class SystemStatsService
    {
        // ── Processeur : variation des temps système entre deux relevés ──
        private ulong _idle, _kernel, _user;
        private bool _cpuPrimed;

        // ── Réseau ──
        private long _rx, _tx;
        private DateTime _netAt = DateTime.MinValue;

        // ── GPU et disque (compteurs de performance Windows) ──
        private List<PerformanceCounter>? _gpuCounters;
        private DateTime _gpuListAt = DateTime.MinValue;
        private bool _gpuUnavailable;
        private PerformanceCounter? _diskCounter;
        private bool _diskUnavailable;

        // ── Processus : temps processeur précédent par identifiant ──
        private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _procPrev = new();

        public bool WithTopProcesses { get; set; } = true;

        public SysSnapshot Sample()
        {
            double cpu = SampleCpu();
            var (ramPercent, used, total) = SampleMemory();
            var (down, up) = SampleNetwork();
            var (topCpu, topMem) = WithTopProcesses ? SampleProcesses() : (new List<ProcInfo>(), new List<ProcInfo>());

            StartPingIfDue();
            return new SysSnapshot(cpu, ramPercent, used, total, SampleGpu(), SampleDisk(), down, up,
                TimeSpan.FromMilliseconds(Environment.TickCount64), SampleTemperature(), topCpu, topMem, _pingMs);
        }

        // ───────────── Latence réseau (ping vers un résolveur public, toutes les 5 s en arrière-plan) ─────────────

        private int? _pingMs;
        private DateTime _pingAt = DateTime.MinValue;
        private bool _pinging;

        private void StartPingIfDue()
        {
            if (_pinging || DateTime.UtcNow - _pingAt < TimeSpan.FromSeconds(5)) return;
            _pinging = true;
            _pingAt = DateTime.UtcNow;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync("1.1.1.1", 1500);
                    _pingMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
                }
                catch { _pingMs = null; }
                finally { _pinging = false; }
            });
        }

        // ───────────── Processeur ─────────────

        private double SampleCpu()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            ulong i = ToUlong(idle), k = ToUlong(kernel), u = ToUlong(user);

            double result = 0;
            if (_cpuPrimed)
            {
                ulong idleDelta = i - _idle, total = (k - _kernel) + (u - _user);   // le temps « kernel » inclut déjà le temps inactif
                result = total == 0 ? 0 : (1.0 - (double)idleDelta / total) * 100;
            }
            _idle = i; _kernel = k; _user = u; _cpuPrimed = true;
            return Math.Clamp(result, 0, 100);
        }

        // ───────────── Mémoire ─────────────

        private static (double Percent, long Used, long Total) SampleMemory()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);
            long total = (long)status.ullTotalPhys, used = total - (long)status.ullAvailPhys;
            return (total == 0 ? 0 : used * 100.0 / total, used, total);
        }

        // ───────────── Réseau (octets par seconde) ─────────────

        private (double Down, double Up) SampleNetwork()
        {
            long rx = 0, tx = 0;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var stats = nic.GetIPv4Statistics();
                    rx += stats.BytesReceived;
                    tx += stats.BytesSent;
                }
            }
            catch { return (0, 0); }

            var now = DateTime.UtcNow;
            double down = 0, up = 0;
            if (_netAt != DateTime.MinValue)
            {
                double seconds = Math.Max(0.2, (now - _netAt).TotalSeconds);
                down = Math.Max(0, (rx - _rx) / seconds);
                up = Math.Max(0, (tx - _tx) / seconds);
            }
            _rx = rx; _tx = tx; _netAt = now;
            return (down, up);
        }

        // ───────────── Carte graphique ─────────────

        /// <summary>Charge 3D de la carte graphique (compteurs « GPU Engine » de Windows) ; null si indisponible.</summary>
        private bool _gpuBuilding;

        private double? SampleGpu()
        {
            if (_gpuUnavailable) return null;

            // La liste des moteurs change avec les processus : elle se reconstruit en arrière-plan, sans retarder les relevés
            bool due = _gpuCounters == null || DateTime.UtcNow - _gpuListAt > TimeSpan.FromSeconds(60);
            if (due && !_gpuBuilding)
            {
                _gpuBuilding = true;
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var category = new PerformanceCounterCategory("GPU Engine");
                        var fresh = category.GetInstanceNames()
                            .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                            .Select(n => { try { return new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true); } catch { return null; } })
                            .Where(c => c != null).Select(c => c!).ToList();
                        foreach (var c in fresh) { try { c.NextValue(); } catch { } }   // premier relevé : amorçage
                        var old = _gpuCounters;
                        _gpuCounters = fresh;
                        _gpuListAt = DateTime.UtcNow;
                        if (fresh.Count == 0) _gpuUnavailable = true;
                        if (old != null) foreach (var c in old) c.Dispose();
                    }
                    catch { _gpuUnavailable = true; }
                    finally { _gpuBuilding = false; }
                });
            }

            var counters = _gpuCounters;
            if (counters == null) return 0;   // pas encore prête : le premier relevé arrive dans un instant
            double sum = 0;
            foreach (var c in counters) { try { sum += c.NextValue(); } catch { } }
            return Math.Clamp(sum, 0, 100);
        }
        private double? SampleDisk()
        {
            if (_diskUnavailable) return null;
            try
            {
                _diskCounter ??= new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
                return Math.Clamp(_diskCounter.NextValue(), 0, 100);
            }
            catch { _diskUnavailable = true; return null; }
        }

        // ───────────── Température (quand l'ACPI l'expose) ─────────────

        private DateTime _tempAt = DateTime.MinValue;
        private double? _tempCache;
        private bool _tempUnavailable;

        private double? SampleTemperature()
        {
            if (_tempUnavailable) return null;
            if (DateTime.UtcNow - _tempAt < TimeSpan.FromSeconds(10)) return _tempCache;
            _tempAt = DateTime.UtcNow;
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (System.Management.ManagementObject o in searcher.Get())
                {
                    double celsius = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                    if (celsius is > 0 and < 130) { _tempCache = celsius; return _tempCache; }
                }
                _tempUnavailable = true;
            }
            catch { _tempUnavailable = true; }
            return null;
        }

        // ───────────── Processus les plus gourmands ─────────────

        private (List<ProcInfo> Cpu, List<ProcInfo> Memory) SampleProcesses()
        {
            var now = DateTime.UtcNow;
            var all = new List<(string Name, double Cpu, long Mem)>();
            var alive = new HashSet<int>();

            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id <= 4) continue;
                        alive.Add(p.Id);

                        var cpuTime = p.TotalProcessorTime;
                        double cpu = 0;
                        if (_procPrev.TryGetValue(p.Id, out var prev))
                        {
                            double seconds = Math.Max(0.2, (now - prev.At).TotalSeconds);
                            cpu = (cpuTime - prev.Cpu).TotalSeconds / seconds / Environment.ProcessorCount * 100;
                        }
                        _procPrev[p.Id] = (cpuTime, now);
                        all.Add((p.ProcessName, Math.Max(0, cpu), p.WorkingSet64));
                    }
                    catch { /* processus protégé ou terminé pendant le relevé */ }
                }
            }

            foreach (var gone in _procPrev.Keys.Where(id => !alive.Contains(id)).ToList()) _procPrev.Remove(gone);

            // Plusieurs processus du même programme (navigateur…) sont regroupés sous un seul nom
            var grouped = all.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProcInfo(g.Key, g.Sum(x => x.Cpu), g.Sum(x => x.Mem))).ToList();

            return (grouped.OrderByDescending(p => p.Cpu).Take(3).ToList(),
                    grouped.OrderByDescending(p => p.MemoryBytes).Take(3).ToList());
        }

        // ───────────── Win32 ─────────────

        private static ulong ToUlong(FILETIME t) => ((ulong)t.dwHighDateTime << 32) | t.dwLowDateTime;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint dwLowDateTime, dwHighDateTime; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    }
}

