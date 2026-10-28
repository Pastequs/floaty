using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Navbar.Services;

namespace Navbar
{
    // Habillage : oreilles du notch, boutons ronds, barres d'égaliseur, anneau du Pomodoro, zone de l'étagère
    public partial class NotchWindow
    {
        private RingGauge? _timerRing;

        private static readonly Color PomodoroRed = Color.FromRgb(0xFF, 0x46, 0x46);
        private static readonly Color BreakGreen = Color.FromRgb(0x33, 0xE0, 0x7A);

        private void InitPillStyle()
        {
            // Boutons du lecteur : précédent et suivant en pastilles discrètes, lecture en pastille pleine
            foreach (var (circle, glyph, size, font) in new (Border, TextBlock, double, double)[]
                     { (PrevCircle, PrevBtn, 30, 13), (PlayCircle, PlayBtn, 38, 16), (NextCircle, NextBtn, 30, 13) })
            {
                circle.Width = circle.Height = size * _scale;
                circle.CornerRadius = new CornerRadius(size / 2 * _scale);
                circle.Margin = new Thickness(3 * _scale, 0, 3 * _scale, 0);
                glyph.FontSize = font * _scale;
            }

            // Égaliseur : trois barres qui dansent quand la lecture est en cours
            foreach (var bar in new[] { Eq1, Eq2, Eq3 })
            {
                bar.Width = 2.4 * _scale;
                bar.Height = 11 * _scale;
                bar.CornerRadius = new CornerRadius(1.2 * _scale);
                bar.Margin = new Thickness(0, 0, 2 * _scale, 0);
            }
            EqBars.Margin = new Thickness(0, 0, 6 * _scale, 0);

            // Étagère : zone en pointillés et bouton « Vider »
            ShelfZone.RadiusX = ShelfZone.RadiusY = 16 * _scale;
            ShelfClear.CornerRadius = new CornerRadius(8 * _scale);
            ShelfClear.Padding = new Thickness(9 * _scale, 2 * _scale, 9 * _scale, 2 * _scale);
            ShelfClear.Margin = new Thickness(10 * _scale, 8 * _scale, 0, 0);
            ShelfClearText.FontSize = 11 * _scale;

            // Eau : bouton réglages en pastille
            WaterSettingsBtn.CornerRadius = new CornerRadius(12 * _scale);
            WaterSettingsBtn.Padding = new Thickness(9 * _scale, 4 * _scale, 11 * _scale, 4 * _scale);
            WaterSettingsBtn.Margin = new Thickness(0, 0, 6 * _scale, 0);
            WaterSettingsGlyph.FontSize = 12 * _scale;
            WaterSettingsText.FontSize = 12 * _scale;
            WaterSettingsText.Margin = new Thickness(6 * _scale, 0, 0, 0);

            // Minuteur : anneau de progression
            _timerRing = new RingGauge(112 * _scale, 6 * _scale);
            TimerRingHost.Child = _timerRing;
            TimerTimeSmall.FontSize = 28 * _scale;
            TimerLabel2.FontSize = 16 * _scale;
            TimerLabel2.Margin = new Thickness(12 * _scale, 0, 0, 0);
            UpdateTimerRing();

            Island.SizeChanged += (_, _) => UpdateEars();
            PlayCircle.Background = Fg();
            PlayBtn.Foreground = new SolidColorBrush(_light ? Colors.White : Colors.Black);   // glyphe sombre sur la pastille claire
            UpdateEars();
        }

        // ───────────── Oreilles du notch ─────────────

        /// <summary>Raccords concaves entre le notch et le haut de l'écran ; leur taille suit celle de la pastille.</summary>
        private void UpdateEars()
        {
            bool glass = _settings.Theme == "glass";
            EarLeft.Visibility = EarRight.Visibility = glass || SideEdge ? Visibility.Collapsed : Visibility.Visible;   // pas d'oreilles sur les côtés
            if (SideEdge) return;
            if (glass) return;

            double height = Island.ActualHeight / _scale / _hf;
            double ear = (4 + 18 * Math.Clamp((height - 9) / 131.0, 0, 1)) * _scale;

            EarLeft.Width = EarLeft.Height = EarRight.Width = EarRight.Height = ear;
            double offset = Island.ActualWidth / 2 + ear / 2 - 0.5;   // -0,5 : pas d'interstice visible avec la pastille
            EarLeftShift.X = -offset;
            EarRightShift.X = offset;
        }

        // ───────────── Égaliseur ─────────────

        private bool _eqRunning, _pillEqRunning;

        /// <summary>Barres verticales de la pastille fermée : elles montent et descendent tant qu'un média est en lecture.</summary>
        private void SetPillEq(bool playing)
        {
            var bars = new[] { (PEq1, PEq1S), (PEq2, PEq2S), (PEq3, PEq3S), (PEq4, PEq4S) };
            foreach (var (bar, _) in bars)
            {
                bar.Width = 2.8 * _scale;
                bar.Height = 15 * _scale;
                bar.CornerRadius = new CornerRadius(1.4 * _scale);
                bar.Margin = new Thickness(0, 0, 2 * _scale, 0);
                bar.Background = EffectiveAccent();
            }
            PillEq.Margin = new Thickness(0, 0, 7 * _scale, 0);
            if (playing == _pillEqRunning) return;
            _pillEqRunning = playing;

            double[] periods = { 0.5, 0.72, 0.58, 0.85 };
            double[] lows = { 0.25, 0.4, 0.2, 0.35 };
            double[] highs = { 1.0, 0.85, 0.95, 0.75 };
            for (int i = 0; i < bars.Length; i++)
            {
                var scale = bars[i].Item2;
                if (!playing) { scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleY = 0.4; continue; }

                var a = new DoubleAnimation(lows[i], highs[i], TimeSpan.FromSeconds(periods[i]))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(a, 20);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }

        private void UpdateEq(bool playing)
        {
            EqBars.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            if (playing == _eqRunning) return;
            _eqRunning = playing;

            var bars = new[] { Eq1S, Eq2S, Eq3S };
            double[] periods = { 0.55, 0.8, 0.65 };
            for (int i = 0; i < bars.Length; i++)
            {
                if (!playing) { bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null); bars[i].ScaleY = 0.4; continue; }

                var a = new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(periods[i]))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(a, 20);
                bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }

        // ───────────── Anneau du minuteur ─────────────

        private void UpdateTimerRing()
        {
            if (_timerRing == null) return;

            double fraction = _timer.Total.TotalSeconds > 0 ? _timer.Remaining.TotalSeconds / _timer.Total.TotalSeconds : 1;
            if (_timer.Status == TimerStatus.Idle) fraction = 1;

            // Rouge pour le focus, vert pour la pause, couleur d'accent pour un simple minuteur
            Color color = _timer.Label == "Focus" ? PomodoroRed
                        : _timer.Label == "Pause" ? BreakGreen
                        : EffectiveAccentColor();
            _timerRing.SetValue(fraction, color, FgAlpha(0x22));
        }

        private void ShelfClear_Click(object sender, MouseButtonEventArgs e)
        {
            _shelf.Clear();
            Storage.Save("shelf", _shelf);
            RenderShelf();
            Shared.NotifyDataChanged(this);
        }
    }
}

