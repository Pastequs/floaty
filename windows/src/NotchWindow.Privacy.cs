using System;
using System.Windows;
using System.Windows.Threading;
using Navbar.Services;

namespace Navbar
{
    // Pastilles de confidentialité (micro, caméra, capture) et ouverture au raccourci Ctrl + Alt + Espace
    public partial class NotchWindow
    {
        private bool _pinnedOpen;                 // ouverte par le raccourci : reste ouverte même sans la souris
        private DispatcherTimer? _pinTimer;

        private void InitPrivacy()
        {
            foreach (var dot in new[] { DotMic, DotCam, DotScreen })
            {
                dot.Width = dot.Height = 8 * _scale;
                dot.Margin = new Thickness(2 * _scale, 0, 2 * _scale, 0);
            }
            PillPrivacy.Margin = new Thickness(6 * _scale, 0, 6 * _scale, 0);

            // Sans activité de la souris, la pastille ouverte au clavier se referme d'elle-même
            _pinTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _pinTimer.Tick += (_, _) => { _pinTimer!.Stop(); _pinnedOpen = false; UpdateState(); };

            Shared.PrivacyChanged += OnPrivacyChanged;
            Shared.HotkeyPressed += OnHotkey;
            Closed += (_, _) =>
            {
                _pinTimer?.Stop();
                Shared.PrivacyChanged -= OnPrivacyChanged;
                Shared.HotkeyPressed -= OnHotkey;
                Shared.WeatherUpdated -= OnWeatherUpdated;
            };
        }

        private void OnPrivacyChanged() => Dispatcher.BeginInvoke(RefreshPill);

        /// <summary>Raccourci global : seule la pastille de l'écran où se trouve la souris réagit.</summary>
        private void OnHotkey()
        {
            var cursorScreen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            if (cursorScreen.DeviceName != ScreenService.Resolve(_screenName).DeviceName) return;

            _pinnedOpen = !_pinnedOpen;
            _pinTimer?.Stop();
            if (_pinnedOpen && !_hovered) _pinTimer?.Start();   // pas de souris : on laisse quelques secondes
            if (_pinnedOpen) { _retractTimer.Stop(); }
            UpdateState();
        }

        /// <summary>Vrai quand au moins une pastille de confidentialité doit s'afficher ; règle leur visibilité.</summary>
        private bool UpdatePrivacyDots()
        {
            var p = Shared.Privacy;
            bool show = _settings.PrivacyDots && p.Any;
            DotMic.Visibility = show && p.Microphone ? Visibility.Visible : Visibility.Collapsed;
            DotCam.Visibility = show && p.Camera ? Visibility.Visible : Visibility.Collapsed;
            DotScreen.Visibility = show && p.Screen ? Visibility.Visible : Visibility.Collapsed;
            PillPrivacy.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PillPrivacy.ToolTip = show && p.Apps.Count > 0 ? string.Join(", ", p.Apps) : null;
            return show;
        }
    }
}
