using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;

namespace Navbar
{
    // Curseurs de volume et de luminosité + interception des touches volume
    public partial class NotchWindow
    {
        private BrightnessService _brightness => Shared.Brightness;
        private SliderRow? _volRow, _briRow;
        private bool _brightnessKnown, _brightnessTouched;

        private void InitControls()
        {
            var accent = EffectiveAccent();
            _volRow = new SliderRow(_scale, "", accent, Fg(), FgAlpha(0x33));
            _briRow = new SliderRow(_scale, "", accent, Fg(), FgAlpha(0x33));
            ControlsList.Children.Add(_volRow.Root);
            ControlsList.Children.Add(_briRow.Root);
            ControlsList.Margin = new Thickness(0, 2 * _scale, 0, 0);
            InitQuickTiles();

            _volRow.Changing += v =>
            {
                _monitor.Volume.SetMute(false);
                _monitor.Volume.SetVolume((float)v);
            };
            _volRow.GlyphClicked += () =>
            {
                _monitor.Volume.ToggleMute();
                RefreshVolume();
            };

            _briRow.Changing += v =>
            {
                _brightnessTouched = true;   // la lecture initiale (lente en DDC) ne doit pas écraser ce choix
                QueueBrightness((int)Math.Round(v * 100));
            };
            _briRow.SetEnabled(false, "…");

            _monitor.VolumeChanged += OnVolumeChanged;
        }

        private void OnVolumeChanged(float v, bool muted) => ShowVolume(v, muted);

        private void ShowVolume(float v, bool muted)
        {
            if (_volRow == null) return;
            _volRow.Glyph.Text = muted || v <= 0.001f ? "" : v < 0.34f ? "" : v < 0.67f ? "" : "";
            _volRow.SetValue(muted ? 0 : v, muted ? "Muet" : null);
        }

        private void RefreshVolume()
        {
            if (_monitor.Volume.TryRead(out float v, out bool muted)) ShowVolume(v, muted);
        }

        /// <summary>Relit volume et luminosité à l'ouverture de l'onglet.</summary>
        private DateTime _controlsAt = DateTime.MinValue;

        private async void RefreshControls()
        {
            if (DateTime.UtcNow - _controlsAt < TimeSpan.FromSeconds(4)) return;   // la page est réaffichée très souvent : pas de relecture à chaque fois
            _controlsAt = DateTime.UtcNow;
            RefreshVolume();
            _ = RefreshQuickTilesAsync();

            _brightnessTouched = false;
            int? b = await Task.Run(() => _brightness.Get());
            _brightnessKnown = b != null;
            if (_brightnessTouched) return;
            if (b != null)
            {
                _briRow?.SetEnabled(true);
                _briRow?.SetValue(b.Value / 100.0);
            }
            else _briRow?.SetEnabled(false, "Indisponible");
        }

        // Les écrans DDC/CI sont lents : on n'envoie que la dernière valeur demandée
        private int _pendingBrightness = -1;
        private int _brightnessBusy;

        private void QueueBrightness(int percent)
        {
            Interlocked.Exchange(ref _pendingBrightness, percent);
            if (Interlocked.Exchange(ref _brightnessBusy, 1) == 1) return;

            Task.Run(() =>
            {
                try
                {
                    int v;
                    while ((v = Interlocked.Exchange(ref _pendingBrightness, -1)) >= 0)
                        _brightness.Set(v);
                }
                finally { Interlocked.Exchange(ref _brightnessBusy, 0); }
            });
        }

        // Touches volume et média : gérées par Shared.ApplyServices() (un seul hook pour tous les écrans)
        private void ApplyKeyHook() => Shared.ApplyServices();

        private void ControlsToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Controls);
    }
}
