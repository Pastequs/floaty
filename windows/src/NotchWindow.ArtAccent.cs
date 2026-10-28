using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Navbar.Services;

namespace Navbar
{
    // Couleur d'accent tirée de la pochette de l'album en lecture (option « Accent selon la pochette »)
    public partial class NotchWindow
    {
        private Color? _artAccent;
        private BitmapSource? _artAccentSource;

        /// <summary>Recalcule l'accent quand la pochette change, puis le réapplique ; sans pochette, l'accent choisi revient.</summary>
        private void UpdateArtAccent(MediaState s)
        {
            if (!_settings.AccentFromArt) { if (_artAccent != null) { _artAccent = null; _artAccentSource = null; ApplyAccent(); RefreshPill(); } return; }
            if (ReferenceEquals(s.Art, _artAccentSource)) return;
            _artAccentSource = s.Art;

            var next = s.Art != null && s.HasMedia ? DominantColor(s.Art) : null;
            if (next == _artAccent) return;
            _artAccent = next;
            ApplyAccent();
            RefreshPill();
        }

        /// <summary>Teinte la plus présente parmi les pixels colorés, rendue assez vive et claire pour se lire sur le fond.</summary>
        private Color? DominantColor(BitmapSource source)
        {
            try
            {
                var small = new TransformedBitmap(source, new ScaleTransform(24.0 / source.PixelWidth, 24.0 / source.PixelHeight));
                var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
                var px = new byte[stride * h];
                bgra.CopyPixels(px, stride, 0);

                double r = 0, g = 0, b = 0, total = 0;
                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    double pb = px[i] / 255.0, pg = px[i + 1] / 255.0, pr = px[i + 2] / 255.0;
                    double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                    double sat = max <= 0 ? 0 : (max - min) / max;
                    double weight = sat * sat * max + 0.0001;   // les pixels ternes ou sombres comptent peu
                    r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
                }
                if (total <= 0) return null;
                return Vivid(r / total, g / total, b / total);
            }
            catch { return null; }
        }

        private Color Vivid(double r, double g, double b)
        {
            // Passage en teinte / saturation / valeur pour imposer un minimum de vivacité et de luminosité
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            double hue = d == 0 ? 0 : max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            double sat = Math.Max(0.55, max <= 0 ? 0 : d / max);
            double val = _light ? Math.Clamp(max, 0.45, 0.72) : Math.Max(0.85, max);

            double c = val * sat, x = c * (1 - Math.Abs(hue % 2 - 1)), m = val - c;
            (double R, double G, double B) = (int)hue switch
            {
                0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
            };
            return Color.FromRgb((byte)Math.Round((R + m) * 255), (byte)Math.Round((G + m) * 255), (byte)Math.Round((B + m) * 255));
        }
    }
}
