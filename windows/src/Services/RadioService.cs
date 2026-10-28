using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace Navbar.Services
{
    public sealed record RadioInfo(RadioKind Kind, bool On, Radio Radio);

    /// <summary>Radios Wi-Fi et Bluetooth de la machine : état et activation.</summary>
    public sealed class RadioService
    {
        public async Task<List<RadioInfo>> GetAsync()
        {
            try
            {
                if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return new List<RadioInfo>();
                var radios = await Radio.GetRadiosAsync();
                return radios
                    .Where(r => r.Kind is RadioKind.WiFi or RadioKind.Bluetooth)
                    .Select(r => new RadioInfo(r.Kind, r.State == RadioState.On, r))
                    .ToList();
            }
            catch { return new List<RadioInfo>(); }
        }

        public async Task<bool> SetAsync(RadioInfo radio, bool on)
        {
            try
            {
                var result = await radio.Radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
                return result == RadioAccessStatus.Allowed;
            }
            catch { return false; }
        }
    }
}
