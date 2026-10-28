using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Enumeration.Pnp;

namespace Navbar.Services
{
    public sealed record BtDevice(string Name, ulong Address, uint ClassOfDevice, bool Connected)
    {
        /// <summary>Classe principale Bluetooth : 4 = audio/vidéo, 5 = périphérique (clavier, souris), 2 = téléphone.</summary>
        public int Major => (int)((ClassOfDevice >> 8) & 0x1F);
        public bool IsAudio => Major == 4;
        public bool IsKeyboard => Major == 5 && (ClassOfDevice & 0x40) != 0;
        public bool IsMouse => Major == 5 && (ClassOfDevice & 0x80) != 0;
    }

    /// <summary>Appareils Bluetooth appairés : liste, connexion, déconnexion et batterie (si Windows la connaît).</summary>
    public sealed class BluetoothService
    {
        // Services Bluetooth activés/désactivés pour connecter ou déconnecter un appareil audio
        private static readonly Guid[] Services =
        {
            new("0000110B-0000-1000-8000-00805F9B34FB"),   // A2DP (musique)
            new("0000111E-0000-1000-8000-00805F9B34FB"),   // mains libres
            new("00001108-0000-1000-8000-00805F9B34FB"),   // casque
        };

        private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

        public List<BtDevice> GetDevices()
        {
            var result = new List<BtDevice>();
            var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                fReturnAuthenticated = 1, fReturnRemembered = 1, fReturnConnected = 1,
                fReturnUnknown = 0, fIssueInquiry = 0, cTimeoutMultiplier = 0, hRadio = IntPtr.Zero,
            };
            var info = NewInfo();

            IntPtr handle = BluetoothFindFirstDevice(ref search, ref info);
            if (handle == IntPtr.Zero) return result;   // pas de radio Bluetooth
            try
            {
                do
                {
                    if (!string.IsNullOrWhiteSpace(info.szName))
                        result.Add(new BtDevice(info.szName, info.Address, info.ulClassofDevice, info.fConnected != 0));
                    info = NewInfo();
                }
                while (BluetoothFindNextDevice(handle, ref info));
            }
            finally { BluetoothFindDeviceClose(handle); }

            return result.OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
        }

        /// <summary>Connecte ou déconnecte un appareil en (dés)activant ses services audio.</summary>
        public void SetConnected(BtDevice device, bool connect)
        {
            var info = NewInfo();
            info.Address = device.Address;

            if (connect)
            {
                foreach (var s in Services) { var g = s; BluetoothSetServiceState(IntPtr.Zero, ref info, ref g, 1); }
                return;
            }

            foreach (var s in Services) { var g = s; BluetoothSetServiceState(IntPtr.Zero, ref info, ref g, 0); }
            System.Threading.Thread.Sleep(400);
            // On remet les services « disponibles » pour que l'appareil puisse se reconnecter plus tard depuis Windows
            foreach (var s in Services) { var g = s; BluetoothSetServiceState(IntPtr.Zero, ref info, ref g, 1); }
        }

        /// <summary>Niveaux de batterie rapportés par Windows, par nom d'appareil.</summary>
        public async Task<Dictionary<string, int>> GetBatteriesAsync()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var props = new[] { "System.ItemNameDisplay", BatteryKey };
                var objects = await PnpObject.FindAllAsync(PnpObjectType.Device, props);
                foreach (var o in objects)
                {
                    if (!o.Id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!o.Properties.TryGetValue(BatteryKey, out var raw) || raw == null) continue;
                    if (!o.Properties.TryGetValue("System.ItemNameDisplay", out var nameObj) || nameObj is not string name) continue;

                    int percent = Convert.ToInt32(raw);
                    if (percent is < 0 or > 100) continue;
                    if (!result.TryGetValue(name, out int known) || percent > known) result[name] = percent;
                }
            }
            catch { /* énumération indisponible : pas de batterie affichée */ }
            return result;
        }

        private static BLUETOOTH_DEVICE_INFO NewInfo() => new()
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
            szName = "",
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public uint dwSize;
            public int fReturnAuthenticated, fReturnRemembered, fReturnUnknown, fReturnConnected, fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public uint dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public int fConnected, fRemembered, fAuthenticated;
            public SYSTEMTIME stLastSeen, stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS search, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(IntPtr find, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothFindDeviceClose(IntPtr find);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern uint BluetoothSetServiceState(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ref Guid service, uint flags);
    }
}
