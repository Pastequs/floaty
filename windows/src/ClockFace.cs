using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Navbar
{
    /// <summary>Horloge dessinée : analogique (aiguilles) ou anneau des secondes avec l'heure au centre.</summary>
    public sealed class ClockFace : Canvas
    {
        private readonly double _size;
        private readonly Ellipse _outline = new();
        private readonly Line _hour = new(), _minute = new(), _second = new();
        private readonly Line[] _ticks = new Line[12];
        private readonly Ellipse _ringTrack = new();
        private readonly System.Windows.Shapes.Path _ringArc = new();
        private readonly TextBlock _center = new();

        public string Mode { get; set; } = "analog";

        public ClockFace(double size)
        {
            _size = size;
            Width = Height = size;

            _outline.Width = _outline.Height = size - 2;
            SetLeft(_outline, 1); SetTop(_outline, 1);
            Children.Add(_outline);

            for (int i = 0; i < _ticks.Length; i++)
            {
                _ticks[i] = new Line { StrokeThickness = i % 3 == 0 ? 1.6 : 1 };
                Children.Add(_ticks[i]);
            }
            foreach (var l in new[] { _hour, _minute, _second })
            {
                l.StrokeStartLineCap = l.StrokeEndLineCap = PenLineCap.Round;
                Children.Add(l);
            }
            _hour.StrokeThickness = size * 0.065;
            _minute.StrokeThickness = size * 0.045;
            _second.StrokeThickness = size * 0.025;

            _ringTrack.Width = _ringTrack.Height = size - 6;
            SetLeft(_ringTrack, 3); SetTop(_ringTrack, 3);
            _ringTrack.StrokeThickness = size * 0.07;
            _ringArc.StrokeThickness = size * 0.07;
            _ringArc.StrokeStartLineCap = _ringArc.StrokeEndLineCap = PenLineCap.Round;
            Children.Add(_ringTrack);
            Children.Add(_ringArc);

            _center.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            _center.FontWeight = FontWeights.SemiBold;
            _center.FontSize = size * 0.25;
            _center.Width = size;
            _center.TextAlignment = TextAlignment.Center;
            SetTop(_center, size * 0.5 - size * 0.17);
            Children.Add(_center);
        }

        public void Update(DateTime now, Brush fg, Brush dim, Brush accent, string timeText)
        {
            bool analog = Mode == "analog";
            foreach (var e in new UIElement[] { _outline, _hour, _minute, _second })
                e.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            foreach (var t in _ticks) t.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            _ringTrack.Visibility = _ringArc.Visibility = _center.Visibility = analog ? Visibility.Collapsed : Visibility.Visible;

            double c = _size / 2;
            if (analog)
            {
                _outline.Stroke = dim;
                _outline.StrokeThickness = 1.4;
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6, inner = c * (i % 3 == 0 ? 0.74 : 0.84), outer = c * 0.93;
                    _ticks[i].Stroke = dim;
                    _ticks[i].X1 = c + inner * Math.Sin(a); _ticks[i].Y1 = c - inner * Math.Cos(a);
                    _ticks[i].X2 = c + outer * Math.Sin(a); _ticks[i].Y2 = c - outer * Math.Cos(a);
                }
                Hand(_hour, c, c * 0.5, (now.Hour % 12 + now.Minute / 60.0) * 30, fg);
                Hand(_minute, c, c * 0.72, (now.Minute + now.Second / 60.0) * 6, fg);
                Hand(_second, c, c * 0.8, now.Second * 6, accent);
                return;
            }

            _ringTrack.Stroke = dim;
            _ringTrack.Fill = Brushes.Transparent;
            _ringArc.Stroke = accent;
            _center.Foreground = fg;
            _center.Text = timeText;

            // Arc de 0 à 360° selon les secondes écoulées dans la minute
            double radius = (_size - 6) / 2, frac = (now.Second + now.Millisecond / 1000.0) / 60.0;
            double angle = Math.Max(0.5, frac * 360) * Math.PI / 180;
            var start = new Point(c, c - radius);
            var end = new Point(c + radius * Math.Sin(angle), c - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
            _ringArc.Data = new PathGeometry(new[] { figure });
        }

        private void Hand(Line line, double c, double length, double degrees, Brush brush)
        {
            double a = degrees * Math.PI / 180;
            line.Stroke = brush;
            line.X1 = c; line.Y1 = c;
            line.X2 = c + length * Math.Sin(a);
            line.Y2 = c - length * Math.Cos(a);
        }
    }
}
