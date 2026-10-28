using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Navbar
{
    /// <summary>Anneau de progression : piste discrète, halo et arc coloré dans le sens horaire.</summary>
    public sealed class RingGauge : Canvas
    {
        private readonly double _size;
        private readonly Ellipse _track = new(), _glow = new();
        private readonly Path _arc = new(), _arcGlow = new();

        public RingGauge(double size, double thickness)
        {
            _size = size;
            Width = Height = size;

            double inset = thickness + 2;
            foreach (var e in new[] { _track, _glow })
            {
                e.Width = e.Height = size - inset * 2;
                SetLeft(e, inset); SetTop(e, inset);
                e.Fill = Brushes.Transparent;
            }
            _track.StrokeThickness = thickness;
            _glow.StrokeThickness = thickness * 2.6;

            foreach (var p in new[] { _arc, _arcGlow })
            {
                p.StrokeStartLineCap = p.StrokeEndLineCap = PenLineCap.Round;
                p.IsHitTestVisible = false;
            }
            _arc.StrokeThickness = thickness;
            _arcGlow.StrokeThickness = thickness * 2.6;

            Children.Add(_glow);
            Children.Add(_track);
            Children.Add(_arcGlow);
            Children.Add(_arc);
        }

        /// <summary>Met à jour l'arc : <paramref name="fraction"/> de 0 à 1, couleur de l'arc, piste en gris neutre.</summary>
        public void SetValue(double fraction, Color color, Brush track)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            _track.Stroke = track;
            _glow.Stroke = Brushes.Transparent;

            var solid = new SolidColorBrush(color);
            var halo = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B));
            _arc.Stroke = solid;
            _arcGlow.Stroke = halo;

            if (fraction <= 0.002) { _arc.Data = _arcGlow.Data = null; return; }

            double inset = _arc.StrokeThickness + 2, radius = (_size - inset * 2) / 2, c = _size / 2;
            double angle = Math.Min(fraction, 0.9999) * 2 * Math.PI;
            var start = new Point(c, c - radius);
            var end = new Point(c + radius * Math.Sin(angle), c - radius * Math.Cos(angle));

            var figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            _arc.Data = _arcGlow.Data = geometry;
        }
    }
}
