using System;
using System.Runtime.InteropServices;

namespace Navbar.Services
{
    /// <summary>Volume système via Core Audio (COM), sans dépendance externe.</summary>
    public sealed class VolumeService
    {
        private IAudioEndpointVolume? _endpoint;
        private DateTime _acquiredAt = DateTime.MinValue;

        public bool TryRead(out float volume, out bool muted)
        {
            volume = 0; muted = false;
            try
            {
                var ep = Ensure();
                if (ep == null) return false;
                ep.GetMasterVolumeLevelScalar(out volume);
                ep.GetMute(out muted);
                return true;
            }
            catch
            {
                Release();
                return false;
            }
        }

        public bool SetVolume(float volume)
        {
            try
            {
                var ep = Ensure();
                if (ep == null) return false;
                var ctx = Guid.Empty;
                ep.SetMasterVolumeLevelScalar(Math.Clamp(volume, 0f, 1f), ref ctx);
                return true;
            }
            catch { Release(); return false; }
        }

        public bool SetMute(bool mute)
        {
            try
            {
                var ep = Ensure();
                if (ep == null) return false;
                var ctx = Guid.Empty;
                ep.SetMute(mute, ref ctx);
                return true;
            }
            catch { Release(); return false; }
        }

        public void ToggleMute()
        {
            if (TryRead(out _, out bool muted)) SetMute(!muted);
        }

        /// <summary>Change le volume d'un pas (touches clavier) ; monter relève aussi le mode muet.</summary>
        public void Step(float delta)
        {
            if (!TryRead(out float v, out bool muted)) return;
            if (muted && delta > 0) SetMute(false);
            SetVolume(v + delta);
        }

        private IAudioEndpointVolume? Ensure()
        {
            // Réacquiert périodiquement pour suivre un changement de périphérique par défaut
            if (_endpoint == null || DateTime.UtcNow - _acquiredAt > TimeSpan.FromSeconds(3))
            {
                Release();
                _endpoint = Acquire();
                _acquiredAt = DateTime.UtcNow;
            }
            return _endpoint;
        }

        private void Release()
        {
            if (_endpoint != null)
            {
                try { Marshal.ReleaseComObject(_endpoint); } catch { }
                _endpoint = null;
            }
        }

        private static IAudioEndpointVolume? Acquire()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out IMMDevice device);
                try
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    device.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out object o);
                    return (IAudioEndpointVolume)o;
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr notify);
            int UnregisterControlChangeNotify(IntPtr notify);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float level, ref Guid context);
            int SetMasterVolumeLevelScalar(float level, ref Guid context);
            int GetMasterVolumeLevel(out float level);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            int GetChannelVolumeLevel(uint channel, out float level);
            int GetChannelVolumeLevelScalar(uint channel, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
