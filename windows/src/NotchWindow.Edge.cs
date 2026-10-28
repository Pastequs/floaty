using System;
using System.Windows;
using System.Windows.Media;

namespace Navbar
{
    // Bord de l'écran : haut (par défaut), bas, gauche ou droite. La pastille s'accroche au bord choisi et s'ouvre vers l'intérieur de l'écran.
    public partial class NotchWindow
    {
        private string Edge => _settings.PillEdge is "bottom" or "left" or "right" ? _settings.PillEdge : "top";
        internal bool SideEdge => Edge is "left" or "right";

        /// <summary>Coins arrondis seulement du côté intérieur : le côté collé au bord de l'écran reste plat.</summary>
        internal CornerRadius CornersFor(double r) => Edge switch
        {
            "bottom" => new CornerRadius(r, r, 0, 0),
            "left" => new CornerRadius(0, r, r, 0),
            "right" => new CornerRadius(r, 0, 0, r),
            _ => new CornerRadius(0, 0, r, r),
        };

        /// <summary>Alignement de la pastille dans sa fenêtre transparente, selon le bord.</summary>
        private void ApplyEdge()
        {
            Island.HorizontalAlignment = Edge == "left" ? HorizontalAlignment.Left : Edge == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            Island.VerticalAlignment = Edge == "bottom" ? VerticalAlignment.Bottom : SideEdge ? VerticalAlignment.Center : VerticalAlignment.Top;

            // Oreilles (raccords concaves) : au bord haut ou bas seulement ; en bas elles sont retournées
            var vertical = Edge == "bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            foreach (var ear in new[] { EarLeft, EarRight })
            {
                ear.VerticalAlignment = vertical;
                ear.LayoutTransform = Edge == "bottom" ? new ScaleTransform(1, -1) : Transform.Identity;
            }
            Island.CornerRadius = CornersFor(Radius);
            GlassSheen.CornerRadius = CornersFor(Radius);
        }

        /// <summary>Rectangle de la pastille à l'écran, en pixels (les barres d'icônes s'y accrochent).</summary>
        internal Rect PillRectPx()
        {
            if (PresentationSource.FromVisual(Island) == null) return Rect.Empty;   // pas encore affichée (création des barres avant la fenêtre)
            var origin = Island.PointToScreen(new Point(0, 0));
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return new Rect(origin.X, origin.Y, Island.ActualWidth * dpi, Island.ActualHeight * dpi);
        }
    }
}

