using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Navbar
{
    /// <summary>Pictogrammes météo dessinés en vectoriel (soleil, lune, nuages, pluie, neige, orage, brouillard).</summary>
    public sealed class WeatherGlyph : Canvas
    {
        private static readonly Brush Sun = Frozen(0xFF, 0xC8, 0x3D), Moon = Frozen(0xF2, 0xE8, 0xB8), Cloud = Frozen(0xC4, 0xCF, 0xE4),
                                      DarkCloud = Frozen(0x8E, 0x9B, 0xB5), Rain = Frozen(0x5A, 0xA9, 0xFF), Snow = Frozen(0xE8, 0xF2, 0xFF),
                                      Bolt = Frozen(0xFF, 0xD2, 0x3F), Fog = Frozen(0xAA, 0xB4, 0xC8);

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public WeatherGlyph(string kind, double size)
        {
            Width = Height = size;
            // Les chaînes de géométrie utilisent le point décimal : on dessine en culture invariante (sinon « 15,5 » casse l'analyse)
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try { Children.Add(new Viewbox { Width = size, Height = size, Child = Draw(kind) }); }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        }

        private static Canvas Draw(string kind)
        {
            var c = new Canvas { Width = 24, Height = 24 };
            switch (kind)
            {
                case "clear": AddSun(c, 12, 12, 5, 1); break;
                case "night": c.Children.Add(Shape("M15,3 A9,9 0 1 0 21,15 A7,7 0 1 1 15,3 Z", Moon, null)); break;
                case "partly": AddSun(c, 8, 8, 3.6, 0.85); AddCloud(c, Cloud, 2, 6); break;
                case "cloudy": AddCloud(c, DarkCloud, 0, 1); AddCloud(c, Cloud, 3, 5); break;
                case "fog":
                    AddCloud(c, Cloud, 2, 0);
                    foreach (var y in new[] { 15.5, 19, 22.2 }) c.Children.Add(Shape($"M4,{y} L20,{y}", null, Fog, 1.7));
                    break;
                case "rain":
                    AddCloud(c, DarkCloud, 2, 0);
                    foreach (var x in new[] { 8.0, 12.5, 17.0 }) c.Children.Add(Shape($"M{x},16 L{x - 1.6},20.5", null, Rain, 1.9));
                    break;
                case "snow":
                    AddCloud(c, Cloud, 2, 0);
                    foreach (var x in new[] { 8.0, 12.5, 17.0 }) c.Children.Add(Shape($"M{x},17 L{x},20.6 M{x - 1.5},18.8 L{x + 1.5},18.8", null, Snow, 1.4));
                    break;
                case "storm":
                    AddCloud(c, DarkCloud, 2, 0);
                    c.Children.Add(Shape("M13,13 L9.5,19 L12.5,19 L11,23.5 L16,17 L13,17 Z", Bolt, null));
                    break;
                default: AddCloud(c, Cloud, 2, 5); break;
            }
            return c;
        }

        private static void AddSun(Canvas c, double cx, double cy, double r, double scale)
        {
            c.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Fill = Sun, RenderTransform = new TranslateTransform(cx - r, cy - r) });
            double inner = r + 1.8, outer = r + 4.0 * scale;
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                string d = string.Format(System.Globalization.CultureInfo.InvariantCulture, "M{0:0.##},{1:0.##} L{2:0.##},{3:0.##}",
                    cx + inner * Math.Cos(a), cy + inner * Math.Sin(a), cx + outer * Math.Cos(a), cy + outer * Math.Sin(a));
                c.Children.Add(Shape(d, null, Sun, 1.6));
            }
        }

        /// <summary>Nuage arrondi, décalé de (dx, dy) dans le cadre de 24 × 24.</summary>
        private static void AddCloud(Canvas c, Brush fill, double dx, double dy)
        {
            var cloud = Shape("M7,17 A4.2,4.2 0 0 1 7.6,8.7 A5.6,5.6 0 0 1 18.4,9.8 A3.6,3.6 0 0 1 17.6,17 Z", fill, null);
            cloud.RenderTransform = new TranslateTransform(dx, dy);
            c.Children.Add(cloud);
        }

        private static Path Shape(string data, Brush? fill, Brush? stroke, double thickness = 1)
        {
            var path = new Path { Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = thickness,
                                  StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            return path;
        }
    }
}
