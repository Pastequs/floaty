using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Navbar
{
    /// <summary>Icône vectorielle de la barre d'onglets (jeu Lucide, grille 24 × 24) ; Foreground colore le trait, comme pour un glyphe.</summary>
    public sealed class TabIcon : Border
    {
        private readonly Path _path = new()
        {
            StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        private readonly Viewbox _box;

        public TabIcon()
        {
            Background = Brushes.Transparent;   // toute la surface reçoit le clic
            Cursor = Cursors.Hand;
            _box = new Viewbox { Child = new Canvas { Width = 24, Height = 24, Children = { _path } }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Child = _box;
            SizeChanged += (_, _) => _box.Width = _box.Height = System.Math.Min(ActualWidth, ActualHeight) * 0.64;
        }

        public string Data { set => _path.Data = Geometry.Parse(value); }

        public Brush Foreground { get => _path.Stroke; set => _path.Stroke = value; }

        /// <summary>Sans effet : présent pour rester compatible avec le code qui règle les glyphes de texte.</summary>
        public double FontSize { get; set; }
    }
}
