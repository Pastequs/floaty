using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Navbar
{
    /// <summary>
    /// Retour au toucher : l'élément se tasse dès l'appui (pas au relâchement) puis revient en douceur, sans rebond.
    /// Les animations partent de la valeur courante : un nouvel appui en plein retour est pris en compte immédiatement.
    /// </summary>
    public static class Press
    {
        public static void Attach(FrameworkElement el, double pressed = 0.93)
        {
            if (el.Tag is "press") return;
            el.Tag = "press";
            var scale = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = scale;

            void To(double target, double ms, IEasingFunction ease)
            {
                var a = new DoubleAnimation { To = target, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease };
                Timeline.SetDesiredFrameRate(a, 60);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }

            var down = new CubicEase { EasingMode = EasingMode.EaseOut };
            var up = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.PreviewMouseLeftButtonDown += (_, _) => To(pressed, 90, down);
            el.PreviewMouseLeftButtonUp += (_, _) => To(1, 240, up);
            el.MouseLeave += (_, _) => { if (scale.ScaleX < 0.999) To(1, 240, up); };
        }
    }
}
